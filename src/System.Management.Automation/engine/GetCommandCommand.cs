/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
using System;
using System.Globalization;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Diagnostics.CodeAnalysis;
using System.Management.Automation.Language;
using System.Reflection;
using Dbg = System.Management.Automation.Diagnostics;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// The get-command cmdlet.  It uses the command discovery APIs to find one or more
    /// commands of the given name.  It returns an instance of CommandInfo for each
    /// command that is found.
    /// </summary>
    ///
    [Cmdlet(VerbsCommon.Get, "Command", DefaultParameterSetName = "CmdletSet", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113309")]
    [OutputType(typeof(AliasInfo), typeof(ApplicationInfo), typeof(FunctionInfo),
                typeof(CmdletInfo), typeof(ExternalScriptInfo), typeof(FilterInfo),
                typeof(WorkflowInfo), typeof(string), typeof(PSObject))]
    public sealed class GetCommandCommand : PSCmdlet
    {
        #region Definitions of cmdlet parameters

        /// <summary>
        /// Gets or sets the path(s) or name(s) of the commands to retrieve
        /// </summary>
        ///
        [Parameter(
            Position = 0,
            ValueFromPipeline = true,
            ValueFromPipelineByPropertyName = true,
            ParameterSetName = "AllCommandSet")]
        [ValidateNotNullOrEmpty]
        public string[] Name
        {
            get
            {
                return names;
            }

            set
            {
                nameContainsWildcard = false;
                names = value;

                if (value != null)
                {
                    foreach (string commandName in value)
                    {
                        if (WildcardPattern.ContainsWildcardCharacters(commandName))
                        {
                            nameContainsWildcard = true;
                            break;
                        }
                    }
                }
            }
        } // Path
        private string[] names;
        private bool nameContainsWildcard;

        /// <summary>
        /// Gets or sets the verb parameter to the cmdlet
        /// </summary>
        /// 
        [Parameter(ValueFromPipelineByPropertyName = true, ParameterSetName = "CmdletSet")]
        public string[] Verb
        {
            get
            {
                return verbs;
            } // get

            set
            {
                if (value == null)
                {
                    value = Utils.EmptyArray<string>();
                }
                verbs = value;
                verbPatterns = null;
            } // set
        } // Verb
        private string[] verbs = Utils.EmptyArray<string>();

        /// <summary>
        /// Gets or sets the noun parameter to the cmdlet
        /// </summary>
        /// 
        [Parameter(ValueFromPipelineByPropertyName = true, ParameterSetName = "CmdletSet")]
        [ArgumentCompleter(typeof(NounArgumentCompleter))]
        public string[] Noun
        {
            get
            {
                return nouns;
            } // get

            set
            {
                if (value == null)
                {
                    value = Utils.EmptyArray<string>();
                }
                nouns = value;
                nounPatterns = null;
            } // set
        } // Noun
        private string[] nouns = Utils.EmptyArray<string>();

        /// <summary>
        /// Gets or sets the PSSnapin/Module parameter to the cmdlet
        /// </summary>
        /// 
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        [Parameter(ValueFromPipelineByPropertyName = true)]
        [Alias("PSSnapin")]
        public string[] Module
        {
            get
            {
                return _modules;
            } // get

            set
            {
                if (value == null)
                {
                    value = Utils.EmptyArray<string>();
                }
                _modules = value;
                _modulePatterns = null;

                isModuleSpecified = true;
            } // set
        }
        private string[] _modules = Utils.EmptyArray<string>();
        private bool isModuleSpecified = false;

        /// <summary>
        /// Gets or sets the FullyQualifiedModule parameter to the cmdlet
        /// </summary>
        /// 
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        [Parameter(ValueFromPipelineByPropertyName = true)]
        public ModuleSpecification[] FullyQualifiedModule
        {
            get
            {
                return _moduleSpecifications;
            }

            set
            {
                if (value != null)
                {
                    _moduleSpecifications = value;
                }
                isFullyQualifiedModuleSpecified = true;
            }
        }
        private ModuleSpecification[] _moduleSpecifications = Utils.EmptyArray<ModuleSpecification>();
        private bool isFullyQualifiedModuleSpecified = false;

        /// <summary>
        /// Gets or sets the type of the command to get
        /// </summary>
        /// 
        [Parameter(ValueFromPipelineByPropertyName = true, ParameterSetName = "AllCommandSet")]
        [Alias("Type")]
        public CommandTypes CommandType
        {
            get
            {
                return commandType;
            } // get

            set
            {
                commandType = value;
                isCommandTypeSpecified = true;
            } // set
        } // Noun
        private CommandTypes commandType = CommandTypes.All;
        private bool isCommandTypeSpecified = false;

        /// <summary>
        /// The parameter representing the total number of commands that will
        /// be returned. If negative, all matching commands that are found will
        /// be returned.
        /// </summary>
        /// 
        [Parameter(ValueFromPipelineByPropertyName = true)]
        public int TotalCount
        {
            get
            {
                return totalCount;
            }
            set
            {
                totalCount = value;
            }
        }
        private int totalCount = -1;

        /// <summary>
        /// The parameter that determines if the CommandInfo or the string
        /// definition of the command is output.
        /// </summary>
        /// 
        [Parameter(ValueFromPipelineByPropertyName = true)]
        public SwitchParameter Syntax
        {
            get
            {
                return usage;
            }

            set
            {
                usage = value;
            }
        }
        private bool usage;

        /// <summary>
        /// This parameter causes the output to be packaged into ShowCommandInfo PSObject types
        /// needed to display GUI command information.
        /// </summary>
        [Parameter()]
        public SwitchParameter ShowCommandInfo
        {
            get;
            set;
        }

        /// <summary>
        /// The parameter that all additional arguments get bound to. These arguments are used
        /// when retrieving dynamic parameters from cmdlets that support them.
        /// </summary>
        /// 
        [Parameter(Position = 1, ValueFromRemainingArguments = true)]
        [AllowNull]
        [AllowEmptyCollection]
        [Alias("Args")]
        public object[] ArgumentList
        {
            get
            {
                return commandArgs;
            }
            set
            {
                commandArgs = value;
            }
        }
        private object[] commandArgs;

        /// <summary>
        /// The parameter that determines if additional matching commands should be returned. 
        /// (Additional matching functions and aliases are returned from module tables)
        /// </summary>
        /// 
        [Parameter(ValueFromPipelineByPropertyName = true)]
        public SwitchParameter All
        {
            get
            {
                return all;
            }

            set
            {
                all = value;
            }
        }
        private bool all;

        /// <summary>
        /// The parameter that determines if additional matching commands from available modules should be returned. 
        /// If set to true, only those commands currently in the session are returned. 
        /// </summary>
        /// 
        [Parameter(ValueFromPipelineByPropertyName = true)]
        public SwitchParameter ListImported
        {
            get
            {
                return listImported;
            }

            set
            {
                listImported = value;
            }
        }
        private bool listImported;

        /// <summary>
        /// The parameter that filters commands returned to only include commands that have a parameter with a name that matches one of the ParameterName's arguments
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] ParameterName
        {
            get { return this._parameterNames; }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }

                this._parameterNames = value;
                this._parameterNameWildcards = SessionStateUtilities.CreateWildcardsFromStrings(
                    this._parameterNames,
                    WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase);
            }
        }
        private Collection<WildcardPattern> _parameterNameWildcards;
        private string[] _parameterNames;
        private HashSet<string> _matchedParameterNames;

        /// <summary>
        /// The parameter that filters commands returned to only include commands that have a parameter of a type that matches one of the ParameterType's arguments
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public PSTypeName[] ParameterType
        {
            get
            {
                return this._parameterTypes;
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }

                // if '...CimInstance#Win32_Process' is specified, then exclude '...CimInstance'
                List<PSTypeName> filteredParameterTypes = new List<PSTypeName>(value.Length);
                for (int i = 0; i < value.Length; i++)
                {
                    PSTypeName ptn = value[i];

                    if (value.Any(otherPtn => otherPtn.Name.StartsWith(ptn.Name + "#", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    if ((i != 0) && (ptn.Type != null) && (ptn.Type.Equals(typeof(object))))
                    {
                        continue;
                    }
                    filteredParameterTypes.Add(ptn);
                }
                this._parameterTypes = filteredParameterTypes.ToArray();
            }
        }
        private PSTypeName[] _parameterTypes;

        #endregion Definitions of cmdlet parameters

        #region Overrides

        /// <summary>
        /// Begin Processing
        /// </summary>
        protected override void BeginProcessing()
        {
            _timer.Start();

            base.BeginProcessing();

            if (ShowCommandInfo.IsPresent && Syntax.IsPresent)
            {
                ThrowTerminatingError(
                    new ErrorRecord(
                        new PSArgumentException(DiscoveryExceptions.GetCommandShowCommandInfoParamError),
                        "GetCommandCannotSpecifySyntaxAndShowCommandInfoTogether",
                        ErrorCategory.InvalidArgument,
                        null));
            }
        }

        /// <summary>
        /// method that implements get-command
        /// </summary>
        /// 
        protected override void ProcessRecord()
        {
            // Module and FullyQualifiedModule should not be specified at the same time.
            // Throw out terminating error if this is the case.
            if (isModuleSpecified && isFullyQualifiedModuleSpecified)
            {
                string errMsg = String.Format(CultureInfo.InvariantCulture, SessionStateStrings.GetContent_TailAndHeadCannotCoexist, "Module", "FullyQualifiedModule");
                ErrorRecord error = new ErrorRecord(new InvalidOperationException(errMsg), "ModuleAndFullyQualifiedModuleCannotBeSpecifiedTogether", ErrorCategory.InvalidOperation, null);
                ThrowTerminatingError(error);
            }

            // Initialize the module patterns
            if (_modulePatterns == null)
            {
                _modulePatterns = SessionStateUtilities.CreateWildcardsFromStrings(Module, WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant);
            }

            switch (ParameterSetName)
            {
                case "CmdletSet":
                    AccumulateMatchingCmdlets();
                    break;

                case "AllCommandSet":
                    AccumulateMatchingCommands();
                    break;

                default:
                    Dbg.Assert(
                        false,
                        "Only the valid parameter set names should be used");
                    break;
            }
        }

        /// <summary>
        /// Writes out the accumulated matching commands
        /// </summary>
        /// 
        protected override void EndProcessing()
        {
            // We do not show the pithy aliases (not of the format Verb-Noun) and applications by default. 
            // We will show them only if the Name, All and totalCount are not specified.
            if ((this.Name == null) && (!all) && totalCount == -1)
            {
                CommandTypes commandTypesToIgnore = 0;

                if (((this.CommandType & CommandTypes.Alias) != CommandTypes.Alias) || (!isCommandTypeSpecified))
                {
                    commandTypesToIgnore |= CommandTypes.Alias;
                }

                if (((this.commandType & CommandTypes.Application) != CommandTypes.Application) ||
                    (!isCommandTypeSpecified))
                {
                    commandTypesToIgnore |= CommandTypes.Application;
                }

                accumulatedResults =
                    accumulatedResults.Where(
                        commandInfo =>
                        (((commandInfo.CommandType & commandTypesToIgnore) == 0) ||
                         (commandInfo.Name.IndexOf('-') > 0))).ToList();

            }

            // report not-found errors for ParameterName and ParameterType if needed
            if ((_matchedParameterNames != null) && (ParameterName != null))
            {
                foreach (string requestedParameterName in ParameterName)
                {
                    if (WildcardPattern.ContainsWildcardCharacters(requestedParameterName))
                    {
                        continue;
                    }
                    if (_matchedParameterNames.Contains(requestedParameterName))
                    {
                        continue;
                    }

                    string errorMessage = string.Format(
                        CultureInfo.InvariantCulture,
                        DiscoveryExceptions.CommandParameterNotFound,
                        requestedParameterName);
                    var exception = new ArgumentException(errorMessage, requestedParameterName);
                    var errorRecord = new ErrorRecord(exception, "CommandParameterNotFound",
                                                      ErrorCategory.ObjectNotFound, requestedParameterName);
                    WriteError(errorRecord);
                }
            }

            // Only sort if they didn't fully specify a name)
            if ((names == null) || (nameContainsWildcard))
            {
                // Use the stable sorting to sort the result list
                accumulatedResults = accumulatedResults.OrderBy(a => a, new CommandInfoComparer()).ToList();
            }

            OutputResultsHelper(accumulatedResults);

            object pssenderInfo = Context.GetVariableValue(SpecialVariables.PSSenderInfoVarPath);
            if ((null != pssenderInfo) && (pssenderInfo is System.Management.Automation.Remoting.PSSenderInfo))
            {
                // Win8: 593295. Exchange has around 1000 cmdlets. During Import-PSSession, 
                // Get-Command  | select-object ..,HelpURI,... is run. HelpURI is a script property
                // which in turn runs Get-Help. Get-Help loads the help content and caches it in the process.
                // This caching is using around 190 MB. During V3, we have implemented HelpURI attribute
                // and this should solve it. In V2, we dont have this attribute and hence 3rd parties
                // run into the same issue. The fix here is to reset help cache whenever get-command is run on
                // a remote endpoint. In the worst case, this will affect get-help to run a little longer
                // after get-command is run..but that should be OK because get-help is used mainly for
                // document reading purposes and not in production.
                Context.HelpSystem.ResetHelpProviders();
            }
        }

        #endregion

        #region Private Methods

        private void OutputResultsHelper(IEnumerable<CommandInfo> results)
        {
            CommandOrigin origin = this.MyInvocation.CommandOrigin;

            int count = 0;
            foreach (CommandInfo result in results)
            {
                count += 1;
                // Only write the command if it is visible to the requestor
                if (SessionState.IsVisible(origin, result))
                {
                    // If the -syntax flag was specified, write the definition as a string
                    // otherwise just return the object...
                    if (Syntax)
                    {
                        if (!String.IsNullOrEmpty(result.Syntax))
                        {
                            PSObject syntax = PSObject.AsPSObject(result.Syntax);

                            syntax.IsHelpObject = true;

                            WriteObject(syntax);
                        }
                    }
                    else
                    {
                        if (ShowCommandInfo.IsPresent)
                        {
                            // Write output as ShowCommandCommandInfo object.
                            WriteObject(
                                ConvertToShowCommandInfo(result));
                        }
                        else
                        {
                            // Write output as normal command info object.
                            WriteObject(result);
                        }
                    }
                }
            }

            _timer.Stop();

            // We want telemtry on commands people look for but don't exist - this should give us an idea
            // what sort of commands people expect but either don't exist, or maybe should be installed by default.
            // The StartsWith is to avoid logging telemetry when suggestion mode checks the
            // current directory for scripts/exes in the current directory and '.' is not in the path.
            if (count == 0 && Name != null && Name.Length > 0 && !Name[0].StartsWith(".\\"))
            {
                Telemetry.Internal.TelemetryAPI.ReportGetCommandFailed(Name, _timer.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// The comparer to sort CommandInfo objects in the result list
        /// </summary>
        private class CommandInfoComparer : IComparer<CommandInfo>
        {
            /// <summary>
            /// Compare two CommandInfo objects first by their command types, and if they
            /// are with the same command type, then we compare their names.
            /// </summary>
            /// <param name="x"></param>
            /// <param name="y"></param>
            /// <returns></returns>
            public int Compare(CommandInfo x, CommandInfo y)
            {
                if ((int)x.CommandType < (int)y.CommandType)
                {
                    return -1;
                }
                else if ((int)x.CommandType > (int)y.CommandType)
                {
                    return 1;
                }
                else
                {
                    return String.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase);
                }
            }
        }

        private void AccumulateMatchingCmdlets()
        {
            this.commandType = CommandTypes.Cmdlet | CommandTypes.Function | CommandTypes.Filter | CommandTypes.Alias | CommandTypes.Workflow | CommandTypes.Configuration;

            Collection<string> commandNames = new Collection<string>();
            commandNames.Add("*");
            AccumulateMatchingCommands(commandNames);
        }

        private bool IsNounVerbMatch(CommandInfo command)
        {
            bool result = false;

            do // false loop
            {
                if (verbPatterns == null)
                {
                    verbPatterns = SessionStateUtilities.CreateWildcardsFromStrings(Verb, WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant);
                }

                if (nounPatterns == null)
                {
                    nounPatterns = SessionStateUtilities.CreateWildcardsFromStrings(Noun, WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant);
                }

                if (!string.IsNullOrEmpty(command.ModuleName))
                {
                    if (isFullyQualifiedModuleSpecified)
                    {
                        if (!_moduleSpecifications.Any(
                                moduleSpecification =>
                                ModuleIntrinsics.IsModuleMatchingModuleSpec(command.Module, moduleSpecification)))
                        {
                            break;
                        }
                    }
                    else if (!SessionStateUtilities.MatchesAnyWildcardPattern(command.ModuleName, _modulePatterns, true))
                    {
                        break;
                    }
                }
                else
                {
                    if (_modulePatterns.Count > 0 || _moduleSpecifications.Any())
                    {
                        // Its not a match if we are filtering on a PSSnapin/Module name but the cmdlet doesn't have one.
                        break;
                    }
                }

                // Get the noun and verb to check...
                string verb;
                string noun;
                CmdletInfo cmdlet = command as CmdletInfo;
                if (cmdlet != null)
                {
                    verb = cmdlet.Verb;
                    noun = cmdlet.Noun;
                }
                else
                {
                    if (!CmdletInfo.SplitCmdletName(command.Name, out verb, out noun))
                        break;
                }

                if (!SessionStateUtilities.MatchesAnyWildcardPattern(verb, verbPatterns, true))
                {
                    break;
                }

                if (!SessionStateUtilities.MatchesAnyWildcardPattern(noun, nounPatterns, true))
                {
                    break;
                }

                result = true;
            } while (false);

            return result;
        }

        /// <summary>
        /// Writes out the commands for the AllCommandSet using the specified CommandType
        /// </summary>
        private void AccumulateMatchingCommands()
        {
            Collection<string> commandNames =
                SessionStateUtilities.ConvertArrayToCollection<string>(this.Name);

            if (commandNames.Count == 0)
            {
                commandNames.Add("*");
            }
            AccumulateMatchingCommands(commandNames);
        }

        private void AccumulateMatchingCommands(IEnumerable<string> commandNames)
        {
            // First set the search options

            SearchResolutionOptions options = SearchResolutionOptions.None;
            if (All)
            {
                options = SearchResolutionOptions.SearchAllScopes;
            }

            if ((this.CommandType & CommandTypes.Alias) != 0)
            {
                options |= SearchResolutionOptions.ResolveAliasPatterns;
            }

            if ((this.CommandType & (CommandTypes.Function | CommandTypes.Filter | CommandTypes.Workflow | CommandTypes.Configuration)) != 0)
            {
                options |= SearchResolutionOptions.ResolveFunctionPatterns;
            }

            foreach (string commandName in commandNames)
            {
                try
                {
                    // Determine if the command name is module-qualified, and search
                    // available modules for the command.
                    string moduleName;
                    string plainCommandName = Utils.ParseCommandName(commandName, out moduleName);
                    bool isModuleQualified = (moduleName != null);

                    // If they've specified a module name, we can do some smarter filtering.
                    // Otherwise, we have to filter everything.
                    if ((this.Module.Length == 1) && (!WildcardPattern.ContainsWildcardCharacters(this.Module[0])))
                    {
                        moduleName = this.Module[0];
                    }

                    bool isPattern = WildcardPattern.ContainsWildcardCharacters(plainCommandName);
                    if (isPattern)
                    {
                        options |= SearchResolutionOptions.CommandNameIsPattern;
                    }

                    // Try to initially find the command in the available commands
                    int count = 0;
                    bool isDuplicate;
                    bool resultFound = FindCommandForName(options, commandName, isPattern, true, ref count, out isDuplicate);

                    // If we didn't find the command, or if it had a wildcard, also see if it
                    // is in an available module
                    if (!resultFound || isPattern)
                    {
                        // If the command name had no wildcards or was module-qualified,
                        // import the module so that we can return the fully structured data.
                        // This uses the same code path as module auto-loading.
                        if ((!isPattern) || (!String.IsNullOrEmpty(moduleName)))
                        {
                            string tempCommandName = commandName;
                            if ((!isModuleQualified) && (!String.IsNullOrEmpty(moduleName)))
                            {
                                tempCommandName = moduleName + "\\" + commandName;
                            }

                            try
                            {
                                CommandDiscovery.LookupCommandInfo(tempCommandName, this.MyInvocation.CommandOrigin, this.Context);
                            }
                            catch (CommandNotFoundException)
                            {
                                // Ignore, LookupCommandInfo doesn't handle wildcards.
                            }

                            resultFound = FindCommandForName(options, commandName, isPattern, false, ref count, out isDuplicate);
                        }
                        // Show additional commands from available modules only if ListImported is not specified
                        else if (!ListImported)
                        {
                            if (TotalCount < 0 || count < TotalCount)
                            {
                                foreach (CommandInfo command in System.Management.Automation.Internal.ModuleUtils.GetMatchingCommands(plainCommandName, this.Context, this.MyInvocation.CommandOrigin, rediscoverImportedModules:true, moduleVersionRequired:isFullyQualifiedModuleSpecified))
                                {
                                    // Cannot pass in "command" by ref (foreach iteration variable)
                                    CommandInfo current = command;

                                    if (IsCommandMatch(ref current, out isDuplicate) && (!IsCommandInResult(current)) && IsParameterMatch(current))
                                    {
                                        accumulatedResults.Add(current);

                                        // Make sure we don't exceed the TotalCount parameter
                                        ++count;

                                        if (TotalCount >= 0 && count >= TotalCount)
                                        {
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // If we are trying to match a single specific command name (no glob characters)
                    // then we need to write an error if we didn't find it.
                    if (!isDuplicate)
                    {
                        if (!resultFound && !isPattern)
                        {
                            CommandNotFoundException e =
                                new CommandNotFoundException(
                                    commandName,
                                    null,
                                    "CommandNotFoundException",
                                    DiscoveryExceptions.CommandNotFoundException);

                            WriteError(
                                new ErrorRecord(
                                    e.ErrorRecord,
                                    e));
                            continue;
                        }
                    }
                }
                catch (CommandNotFoundException exception)
                {
                    WriteError(
                        new ErrorRecord(
                            exception.ErrorRecord,
                            exception));
                }
            }
        }

        private bool FindCommandForName(SearchResolutionOptions options, string commandName, bool isPattern, bool emitErrors, ref int currentCount, out bool isDuplicate)
        {
            CommandSearcher searcher =
                    new CommandSearcher(
                        commandName,
                        options,
                        this.CommandType,
                        this.Context);
            
            bool resultFound = false;
            isDuplicate = false;

            do
            {
                try
                {
                    if (!searcher.MoveNext())
                    {
                        break;
                    }
                }
                catch (ArgumentException argumentException)
                {
                    if (emitErrors)
                    {
                        WriteError(new ErrorRecord(argumentException, "GetCommandInvalidArgument", ErrorCategory.SyntaxError, null));
                    }
                    continue;
                }
                catch (PathTooLongException pathTooLong)
                {
                    if (emitErrors)
                    {
                        WriteError(new ErrorRecord(pathTooLong, "GetCommandInvalidArgument", ErrorCategory.SyntaxError, null));
                    }
                    continue;
                }
                catch (FileLoadException fileLoadException)
                {
                    if (emitErrors)
                    {
                        WriteError(new ErrorRecord(fileLoadException, "GetCommandFileLoadError", ErrorCategory.ReadError, null));
                    }
                    continue;
                }
                catch (MetadataException metadataException)
                {
                    if (emitErrors)
                    {
                        WriteError(new ErrorRecord(metadataException, "GetCommandMetadataError", ErrorCategory.MetadataError, null));
                    }
                    continue;
                }
                catch (FormatException formatException)
                {
                    if (emitErrors)
                    {
                        WriteError(new ErrorRecord(formatException, "GetCommandBadFileFormat", ErrorCategory.InvalidData, null));
                    }
                    continue;
                }

                CommandInfo current = ((IEnumerator<CommandInfo>)searcher).Current;

                // skip private commands as early as possible 
                // (i.e. before setting "result found" flag and before trying to use ArgumentList parameter)
                // see bugs Windows 7: #520498 and #520470
                CommandOrigin origin = this.MyInvocation.CommandOrigin;
                if (!SessionState.IsVisible(origin, current))
                {
                    continue;
                }

                bool tempResultFound = IsCommandMatch(ref current, out isDuplicate);

                if (tempResultFound && (!IsCommandInResult(current)))
                {
                    resultFound = true;
                    if (IsParameterMatch(current))
                    {
                        // Make sure we don't exceed the TotalCount parameter
                        ++currentCount;

                        if (TotalCount >= 0 && currentCount > TotalCount)
                        {
                            break;
                        }

                        accumulatedResults.Add(current);

                        if (ArgumentList != null)
                        {
                            // Don't iterate the enumerator any more. If -arguments was specified, then we stop at the first match
                            break;
                        }
                    }


                    // Only for this case, the loop should exit
                    // Get-Command Foo
                    if (isPattern || All || totalCount != -1 || isCommandTypeSpecified || isModuleSpecified || isFullyQualifiedModuleSpecified)
                    {
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }
            } while (true);


            if (All)
            {
                // Get additional matching commands from module tables.
                foreach (CommandInfo command in GetMatchingCommandsFromModules(commandName))
                {
                    CommandInfo c = command;
                    bool tempResultFound = IsCommandMatch(ref c, out isDuplicate);
                    if (tempResultFound)
                    {
                        resultFound = true;
                        if (!IsCommandInResult(command) && IsParameterMatch(c))
                        {
                            ++currentCount;

                            if (TotalCount >= 0 && currentCount > TotalCount)
                            {
                                break;
                            }
                            accumulatedResults.Add(c);
                        }
                        // Make sure we don't exceed the TotalCount parameter
                    }
                }
            }
        
            return resultFound;
        }

        /// <summary>
        /// Determines if the specific command information has already been 
        /// written out based on the path or definition.
        /// </summary>
        /// 
        /// <param name="info">
        /// The command information to check for duplication.
        /// </param>
        /// 
        /// <returns>
        /// true if the command has already been written out.
        /// </returns>
        /// 
        private bool IsDuplicate(CommandInfo info)
        {
            bool result = false;
            string key = null;

            do // false loop
            {
                ApplicationInfo appInfo = info as ApplicationInfo;
                if (appInfo != null)
                {
                    key = appInfo.Path;
                    break;
                }

                CmdletInfo cmdletInfo = info as CmdletInfo;
                if (cmdletInfo != null)
                {
                    key = cmdletInfo.FullName;
                    break;
                }

                ScriptInfo scriptInfo = info as ScriptInfo;
                if (scriptInfo != null)
                {
                    key = scriptInfo.Definition;
                    break;
                }

                ExternalScriptInfo externalScriptInfo = info as ExternalScriptInfo;
                if (externalScriptInfo != null)
                {
                    key = externalScriptInfo.Path;
                    break;
                }
            } while (false);

            if (key != null)
            {
                if (commandsWritten.ContainsKey(key))
                {
                    result = true;
                }
                else
                {
                    commandsWritten.Add(key, info);
                }
            }

            return result;
        }

        private bool IsParameterMatch(CommandInfo commandInfo)
        {
            if ((this.ParameterName == null) && (this.ParameterType == null))
            {
                return true;
            }

            if (this._matchedParameterNames == null)
            {
                this._matchedParameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            IEnumerable<ParameterMetadata> commandParameters = null;
            try
            {
                IDictionary<string, ParameterMetadata> tmp = commandInfo.Parameters;
                if (tmp != null)
                {
                    commandParameters = tmp.Values;
                }
            }
            catch (Exception e)
            {
                CommandProcessor.CheckForSevereException(e);
                // ignore all exceptions when getting parameter metadata (i.e. parse exceptions, dangling alias exceptions)
                // and proceed as if there was no parameter metadata
            }
            if (commandParameters == null)
            {
                // do not match commands which have not been imported yet / for which we don't have parameter metadata yet
                return false;
            }
            else
            {
                bool foundMatchingParameter = false;
                foreach (ParameterMetadata parameterMetadata in commandParameters)
                {
                    if (IsParameterMatch(parameterMetadata))
                    {
                        foundMatchingParameter = true;
                        // not breaking out of the loop early, to ensure that _matchedParameterNames gets populated for all command parameters
                    }
                }
                return foundMatchingParameter;
            }
        }

        private bool IsParameterMatch(ParameterMetadata parameterMetadata)
        {
            //
            // ParameterName matching
            //

            bool nameIsDirectlyMatching = SessionStateUtilities.MatchesAnyWildcardPattern(parameterMetadata.Name, _parameterNameWildcards, true);

            bool oneOfAliasesIsMatching = false;
            foreach (string alias in parameterMetadata.Aliases ?? Enumerable.Empty<string>())
            {
                if (SessionStateUtilities.MatchesAnyWildcardPattern(alias, _parameterNameWildcards, true))
                {
                    _matchedParameterNames.Add(alias);
                    oneOfAliasesIsMatching = true;
                    // don't want to break out of the loop early (need to fully populate _matchedParameterNames hashset)
                }
            }

            bool nameIsMatching = nameIsDirectlyMatching || oneOfAliasesIsMatching;
            if (nameIsMatching)
            {
                _matchedParameterNames.Add(parameterMetadata.Name);
            }

            //
            // ParameterType matching
            //

            bool typeIsMatching;
            if ((this._parameterTypes == null) || (this._parameterTypes.Length == 0))
            {
                typeIsMatching = true;
            }
            else
            {
                typeIsMatching = false;
                if (_parameterTypes != null &&
                    _parameterTypes.Length > 0)
                {
                    typeIsMatching |= _parameterTypes.Any(parameterMetadata.IsMatchingType);
                }
            }

            return nameIsMatching && typeIsMatching;
        }

        private bool IsCommandMatch(ref CommandInfo current, out bool isDuplicate)
        {
            bool isCommandMatch = false;
            isDuplicate = false;

            // Be sure we haven't already found this command before
            if (!IsDuplicate(current))
            {
                if ((current.CommandType & this.CommandType) != 0)
                {
                    isCommandMatch = true;
                }

                // If the command in question is a cmdlet or (a function/filter/workflow/configuration/alias and we are filtering on nouns or verbs),
                // then do the verb/moun check

                if (current.CommandType == CommandTypes.Cmdlet ||
                    ((verbs.Length > 0 || nouns.Length > 0) &&
                     (current.CommandType == CommandTypes.Function ||
                      current.CommandType == CommandTypes.Filter ||
                      current.CommandType == CommandTypes.Workflow ||
                      current.CommandType == CommandTypes.Configuration ||
                      current.CommandType == CommandTypes.Alias)))
                {
                    if (!IsNounVerbMatch(current))
                    {
                        isCommandMatch = false;
                    }
                }
                else
                {
                    if (isFullyQualifiedModuleSpecified)
                    {
                        bool foundModuleMatch = false;
                        foreach (var moduleSpecification in _moduleSpecifications)
                        {
                            if (ModuleIntrinsics.IsModuleMatchingModuleSpec(current.Module, moduleSpecification))
                            {
                                foundModuleMatch = true;
                                break;
                            }
                        }

                        if (!foundModuleMatch)
                        {
                            isCommandMatch = false;
                        }
                    }
                    else if (_modulePatterns != null && _modulePatterns.Count > 0)
                    {
                        if (!SessionStateUtilities.MatchesAnyWildcardPattern(current.ModuleName, _modulePatterns, true))
                        {
                            isCommandMatch = false;
                        }
                    }

                }

                if (isCommandMatch)
                {
                    if (ArgumentList != null)
                    {
                        AliasInfo ai = current as AliasInfo;
                        if (ai != null)
                        {
                            // If the matching command was an alias, then use the resolved command
                            // instead of the alias...
                            current = ai.ResolvedCommand;
                            if (current == null)
                            {
                                return false;
                            }
                        }
                        else if (!(current is CmdletInfo || current is IScriptCommandInfo))
                        {
                            // If current is not a cmdlet or script, we need to throw a terminating error.
                            ThrowTerminatingError(
                                new ErrorRecord(
                                    PSTraceSource.NewArgumentException(
                                        "ArgumentList",
                                        DiscoveryExceptions.CommandArgsOnlyForSingleCmdlet),
                                    "CommandArgsOnlyForSingleCmdlet",
                                    ErrorCategory.InvalidArgument,
                                    current));
                        }
                    }

                    // If the command implements dynamic parameters
                    // then we must make a copy of the CommandInfo which merges the
                    // dynamic parameter metadata with the statically defined parameter
                    // metadata
                    bool needCopy = false;
                    try
                    {
                        // We can ignore some errors that occur when checking if
                        // the command implements dynamic parameters.
                        needCopy = current.ImplementsDynamicParameters;
                    }
                    catch (PSSecurityException)
                    {
                        // Ignore execution policies in get-command, those will get
                        // raised when trying to run the real command
                    }
                    catch (RuntimeException)
                    {
                        // Ignore parse/runtime exceptions.  Again, they will get
                        // raised again if the script is actually run.
                    }

                    if (needCopy)
                    {
                        try
                        {
                            CommandInfo newCurrent = current.CreateGetCommandCopy(ArgumentList);

                            if (ArgumentList != null)
                            {
                                // We need to prepopulate the parameter metadata in the CmdletInfo to
                                // ensure there are no errors. Getting the ParameterSets property
                                // triggers the parameter metadata to be generated
                                ReadOnlyCollection<CommandParameterSetInfo> parameterSets =
                                    newCurrent.ParameterSets;
                            }

                            current = newCurrent;
                        }
                        catch (MetadataException metadataException)
                        {
                            // A metadata exception can be thrown if the dynamic parameters duplicates a parameter
                            // of the cmdlet.

                            WriteError(new ErrorRecord(metadataException, "GetCommandMetadataError",
                                                       ErrorCategory.MetadataError, current));
                        }
                        catch (ParameterBindingException parameterBindingException)
                        {
                            // if the exception is thrown when retrieving dynamic parameters, ignore it and
                            // the static parameter info will be used.
                            if (!parameterBindingException.ErrorRecord.FullyQualifiedErrorId.StartsWith(
                                "GetDynamicParametersException", StringComparison.Ordinal))
                            {
                                throw;
                            }
                        }
                    }
                }
            }
            else
            {
                isDuplicate = true;
            }
            return isCommandMatch;
        }

        /// <summary>
        /// Gets matching commands from the module tables 
        /// </summary>
        /// 
        /// <param name="commandName">
        /// The commandname to look for
        /// </param>
        /// 
        /// <returns>
        /// IEnumerable of CommandInfo objects
        /// </returns>
        /// 
        private IEnumerable<CommandInfo> GetMatchingCommandsFromModules(string commandName)
        {
            WildcardPattern matcher = WildcardPattern.Get(
                                        commandName,
                                        WildcardOptions.IgnoreCase);

            // Use ModuleTableKeys list in reverse order 
            for (int i = Context.EngineSessionState.ModuleTableKeys.Count - 1; i >= 0; i--)
            {
                PSModuleInfo module = null;

                if (Context.EngineSessionState.ModuleTable.TryGetValue(Context.EngineSessionState.ModuleTableKeys[i], out module) == false)
                {
                    Dbg.Assert(false, "ModuleTableKeys should be in sync with ModuleTable");
                }
                else
                {
                    bool isModuleMatch = false;  
                    if (!isFullyQualifiedModuleSpecified)
                    {
                        isModuleMatch = SessionStateUtilities.MatchesAnyWildcardPattern(module.Name, _modulePatterns, true);
                    }
                    else if(_moduleSpecifications.Any(moduleSpecification => ModuleIntrinsics.IsModuleMatchingModuleSpec(module, moduleSpecification)))
                    {
                        isModuleMatch = true;
                    }

                    if (isModuleMatch)
                    {
                        if (module.SessionState != null)
                        {
                            // Look in function table
                            if ((this.CommandType & (CommandTypes.Function | CommandTypes.Filter | CommandTypes.Configuration)) != 0)
                            {
                                foreach (DictionaryEntry function in module.SessionState.Internal.GetFunctionTable())
                                {
                                    FunctionInfo func = (FunctionInfo)function.Value;

                                    if (matcher.IsMatch((string)function.Key) && func.IsImported)
                                    {
                                        // make sure function doesn't come from the current module's nested module
                                        if (func.Module.Path.Equals(module.Path, StringComparison.OrdinalIgnoreCase))
                                            yield return (CommandInfo)function.Value;
                                    }
                                }
                            }

                            // Look in alias table
                            if ((this.CommandType & CommandTypes.Alias) != 0)
                            {
                                foreach (var alias in module.SessionState.Internal.GetAliasTable())
                                {
                                    if (matcher.IsMatch(alias.Key) && alias.Value.IsImported)
                                    {
                                        // make sure alias doesn't come from the current module's nested module
                                        if (alias.Value.Module.Path.Equals(module.Path, StringComparison.OrdinalIgnoreCase))
                                            yield return alias.Value;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Determines if the specific command information has already been 
        /// added to the result from CommandSearcher
        /// </summary>
        /// 
        /// <param name="command">
        /// The command information to check for duplication.
        /// </param>
        /// 
        /// <returns>
        /// true if the command is present in the result.
        /// </returns>
        /// 
        private bool IsCommandInResult(CommandInfo command)
        {
            bool isPresent = false;
            bool commandHasModule = command.Module != null;
            foreach (CommandInfo commandInfo in accumulatedResults)
            {
                if ((command.CommandType == commandInfo.CommandType &&
                     (String.Compare(command.Name, commandInfo.Name, StringComparison.CurrentCultureIgnoreCase) == 0 ||
                    // If the command has been imported with a prefix, then just checking the names for duplication will not be enough. 
                    // Hence, an additional check is done with the prefix information
                      String.Compare(ModuleCmdletBase.RemovePrefixFromCommandName(commandInfo.Name, commandInfo.Prefix), command.Name, StringComparison.CurrentCultureIgnoreCase) == 0)
                    ) && commandInfo.Module != null && commandHasModule &&
                    ( // We do reference equal comparison if both command are imported. If either one is not imported, we compare the module path
                     (commandInfo.IsImported && command.IsImported && commandInfo.Module.Equals(command.Module)) ||
                     ((!commandInfo.IsImported || !command.IsImported) && commandInfo.Module.Path.Equals(command.Module.Path, StringComparison.OrdinalIgnoreCase))
                    ))
                {
                    isPresent = true;
                    break;
                }
            }
            return isPresent;
        }

        #endregion

        #region Members

        private Dictionary<string, CommandInfo> commandsWritten =
            new Dictionary<string, CommandInfo>(StringComparer.OrdinalIgnoreCase);

        private List<CommandInfo> accumulatedResults = new List<CommandInfo>();

        // These members are the collection of wildcard patterns for the "CmdletSet"
        Collection<WildcardPattern> verbPatterns;
        Collection<WildcardPattern> nounPatterns;
        Collection<WildcardPattern> _modulePatterns;

        private Stopwatch _timer = new Stopwatch();

        #endregion

        #region ShowCommandInfo support

        // Converts to PSObject containing ShowCommand information.
        private static PSObject ConvertToShowCommandInfo(CommandInfo cmdInfo)
        {
            PSObject showCommandInfo = new PSObject();
            showCommandInfo.Properties.Add(new PSNoteProperty("Name", cmdInfo.Name));
            showCommandInfo.Properties.Add(new PSNoteProperty("ModuleName", cmdInfo.ModuleName));
            showCommandInfo.Properties.Add(new PSNoteProperty("Module", GetModuleInfo(cmdInfo)));
            showCommandInfo.Properties.Add(new PSNoteProperty("CommandType", cmdInfo.CommandType));
            showCommandInfo.Properties.Add(new PSNoteProperty("Definition", cmdInfo.Definition));
            showCommandInfo.Properties.Add(new PSNoteProperty("ParameterSets", GetParameterSets(cmdInfo)));

            return showCommandInfo;
        }

        private static PSObject GetModuleInfo(CommandInfo cmdInfo)
        {
            PSObject moduleInfo = new PSObject();
            string moduleName = (cmdInfo.Module != null) ? cmdInfo.Module.Name : string.Empty;
            moduleInfo.Properties.Add(new PSNoteProperty("Name", moduleName));

            return moduleInfo;
        }

        private static PSObject[] GetParameterSets(CommandInfo cmdInfo)
        {
            ReadOnlyCollection<CommandParameterSetInfo> parameterSets = null;
            try
            {
                if (cmdInfo.ParameterSets != null)
                {
                    parameterSets = cmdInfo.ParameterSets;
                }
            }
            catch (InvalidOperationException) { }
            catch (PSNotSupportedException) { }
            catch (PSNotImplementedException) { }

            if (parameterSets == null)
            {
                return Utils.EmptyArray<PSObject>();
            }

            List<PSObject> returnParameterSets = new List<PSObject>(cmdInfo.ParameterSets.Count);

            foreach (CommandParameterSetInfo parameterSetInfo in parameterSets)
            {
                PSObject parameterSetObj = new PSObject();
                parameterSetObj.Properties.Add(new PSNoteProperty("Name", parameterSetInfo.Name));
                parameterSetObj.Properties.Add(new PSNoteProperty("IsDefault", parameterSetInfo.IsDefault));
                parameterSetObj.Properties.Add(new PSNoteProperty("Parameters", GetParameterInfo(parameterSetInfo.Parameters)));

                returnParameterSets.Add(parameterSetObj);
            }

            return returnParameterSets.ToArray();
        }

        private static PSObject[] GetParameterInfo(ReadOnlyCollection<CommandParameterInfo> parameters)
        {
            List<PSObject> parameterObjs = new List<PSObject>(parameters.Count);
            foreach (CommandParameterInfo parameter in parameters)
            {
                PSObject parameterObj = new PSObject();
                parameterObj.Properties.Add(new PSNoteProperty("Name", parameter.Name));
                parameterObj.Properties.Add(new PSNoteProperty("IsMandatory", parameter.IsMandatory));
                parameterObj.Properties.Add(new PSNoteProperty("ValueFromPipeline", parameter.ValueFromPipeline));
                parameterObj.Properties.Add(new PSNoteProperty("Position", parameter.Position));
                parameterObj.Properties.Add(new PSNoteProperty("ParameterType", GetParameterType(parameter.ParameterType)));

                bool hasParameterSet = false;
                IList<string> validValues = new List<string>();
                var validateSetAttribute = parameter.Attributes.Where(x=>(x is ValidateSetAttribute)).Cast<ValidateSetAttribute>().LastOrDefault();
                if (validateSetAttribute != null)
                {
                    hasParameterSet = true;
                    validValues = validateSetAttribute.ValidValues;
                }
                parameterObj.Properties.Add(new PSNoteProperty("HasParameterSet", hasParameterSet));
                parameterObj.Properties.Add(new PSNoteProperty("ValidParamSetValues", validValues));

                parameterObjs.Add(parameterObj);
            }

            return parameterObjs.ToArray();
        }

        private static PSObject GetParameterType(Type parameterType)
        {
            PSObject returnParameterType = new PSObject();
            bool isEnum = parameterType.GetTypeInfo().IsEnum;
            bool isArray = parameterType.GetTypeInfo().IsArray;
            returnParameterType.Properties.Add(new PSNoteProperty("FullName", parameterType.FullName));
            returnParameterType.Properties.Add(new PSNoteProperty("IsEnum", isEnum));
            returnParameterType.Properties.Add(new PSNoteProperty("IsArray", isArray));

            ArrayList enumValues = (isEnum) ?
                new ArrayList(Enum.GetValues(parameterType)) : new ArrayList();
            returnParameterType.Properties.Add(new PSNoteProperty("EnumValues", enumValues));

            bool hasFlagAttribute = (isArray) ?
                ((parameterType.GetTypeInfo().GetCustomAttributes(typeof(FlagsAttribute), true)).Count() > 0) : false;
            returnParameterType.Properties.Add(new PSNoteProperty("HasFlagAttribute", hasFlagAttribute));

            // Recurse into array elements.
            object elementType = (isArray) ?
                GetParameterType(parameterType.GetElementType()) : null;
            returnParameterType.Properties.Add(new PSNoteProperty("ElementType", elementType));

            bool implementsDictionary = (!isEnum && !isArray && (parameterType is IDictionary));
            returnParameterType.Properties.Add(new PSNoteProperty("ImplementsDictionary", implementsDictionary));

            return returnParameterType;
        }

        #endregion
    }

    /// <summary>
    /// 
    /// </summary>
    public class NounArgumentCompleter : IArgumentCompleter
    {
        /// <summary>
        /// 
        /// </summary>
        public IEnumerable<CompletionResult> CompleteArgument(string commandName, string parameterName, string wordToComplete, CommandAst commandAst, IDictionary fakeBoundParameters)
        {
	        if (fakeBoundParameters == null)
	        {
		        throw PSTraceSource.NewArgumentNullException("fakeBoundParameters");
	        }

            var commandInfo = new CmdletInfo("Get-Command", typeof(GetCommandCommand));
            var ps = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace).
                AddCommand(commandInfo).
                AddParameter("Noun", wordToComplete + "*");

            if (fakeBoundParameters.Contains("Module"))
            {
                ps.AddParameter("Module", fakeBoundParameters["Module"]);
            }

            HashSet<string> nouns = new HashSet<string>();
            var results = ps.Invoke<CommandInfo>();
            foreach (var result in results)
            {
                var dash = result.Name.IndexOf('-');
                if (dash != -1)
                {
                    nouns.Add(result.Name.Substring(dash + 1));
                }
            }

            return nouns.OrderBy(noun => noun).Select(noun => new CompletionResult(noun, noun, CompletionResultType.Text, noun));
        }
    }
}