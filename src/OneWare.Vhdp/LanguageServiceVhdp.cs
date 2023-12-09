using System.Reactive.Disposables;
using System.Reactive.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OneWare.SDK.Enums;
using OneWare.SDK.LanguageService;
using OneWare.SDK.Models;
using OneWare.SDK.Services;
using OneWare.SDK.ViewModels;
using Prism.Ioc;
using VHDPlus.Analyzer;
using VHDPlus.Analyzer.Checks;
using VHDPlus.Analyzer.Diagnostics;
using VHDPlus.Analyzer.Elements;
using VHDPlus.Analyzer.Info;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace OneWare.Vhdp;

public class LanguageServiceVhdp(string workspace) : LanguageServiceBase("VHDP", workspace)
{
    private int _version = 0;
    public HdpProjectContext HdpProjectContext { get; } = new(workspace);

    private CompositeDisposable _compositeDisposable = new();

    public override Task ActivateAsync()
    {
        Observable.FromEventPattern<string>(HdpProjectContext, nameof(HdpProjectContext.DiagnosticsChanged)).Subscribe(x =>
        {
            RefreshDiagnostics(x.EventArgs);
        }).DisposeWith(_compositeDisposable);
        
        IsActivated = true;
        IsLanguageServiceReady = true;
        return base.ActivateAsync();
    }

    public override Task DeactivateAsync()
    {
        _compositeDisposable.Dispose();
        _compositeDisposable = new CompositeDisposable();
        IsActivated = false;
        IsLanguageServiceReady = false;
        return base.DeactivateAsync();
    }

    public override ITypeAssistance GetTypeAssistance(IEditor editor)
    {
        return new TypeAssistanceVhdp(editor, this);
    }

    public override void RefreshTextDocument(string fullPath, Container<TextDocumentContentChangeEvent> changes)
    {
        base.RefreshTextDocument(fullPath, changes);
        HdpProjectContext.ProcessChanges(fullPath, changes);
    }

    public override void RefreshTextDocument(string fullPath, string newText)
    {
        base.RefreshTextDocument(fullPath, newText);
        HdpProjectContext.ProcessChanges(fullPath, newText);
    }
    
    private void RefreshDiagnostics(string fullPath)
    {
        var context = HdpProjectContext.GetContext(fullPath);
        
        PublishDiag(new PublishDiagnosticsParams()
        {
            Uri = DocumentUri.Parse(fullPath),
            Version = _version,
            Diagnostics = new Container<Diagnostic>(context.Diagnostics.Select(x => new Diagnostic { 
                Message = x.Message,
                Range = new Range(x.StartLine, x.StartCol, x.EndLine, x.EndCol),
                Source = Name,
                Severity = x.Level switch
                {
                    DiagnosticLevel.Error => DiagnosticSeverity.Error,
                    DiagnosticLevel.Warning => DiagnosticSeverity.Warning,
                    _ => DiagnosticSeverity.Hint
                }
            }).ToArray())
        });
    }
    
    public override Task<Hover?> RequestHoverAsync(string fullPath,
        Position pos)
    {
        var context = HdpProjectContext.GetContext(fullPath);

        var offset = context.GetOffset(pos.Line, pos.Character + 1);
        var segment = AnalyzerHelper.GetSegmentFromOffset(context, offset);

        var info = "";

        var error = context.Diagnostics.OrderBy(x => x.Level)
            .FirstOrDefault(error =>
                pos.Line >= error.StartLine && pos.Character >= error.StartCol && pos.Line < error.EndLine ||
                pos.Line == error.EndLine && pos.Character <= error.EndCol);

        if (error != null) info += error.Message + "\n";

        if (segment != null)
        {
            if (segment.ConcatOperator != null && offset >= segment.ConcatOperatorIndex &&
                offset <= segment.ConcatOperatorIndex + segment.ConcatOperator.Length)
            {
                info += SegmentInfo.GetInfoConcatMarkdown(segment.ConcatOperator);
            }
            else info += SegmentInfo.GetInfoMarkdown(segment);
        }


        var hover = new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = info,
            }),
            //Range = new Range(segment.Context.GetLine(segment.Offset), segment.Context.GetCol(segment.Offset), 
            //   segment.Context.GetLine(segment.EndOffset), segment.Context.GetCol(segment.EndOffset)),
        };
        return Task.FromResult<Hover?>(hover);
    }

    public override async Task<SignatureHelp?> RequestSignatureHelpAsync(string fullPath, Position pos, SignatureHelpTriggerKind triggerKind, string? triggerChar,
        bool isRetrigger, SignatureHelp? activeSignatureHelp)
    {
        await HdpProjectContext.AnalyzeAsync(fullPath,AnalyzerMode.Indexing | AnalyzerMode.Resolve | AnalyzerMode.Check);
        
        var context = HdpProjectContext.GetContext(fullPath);
        
        var segment = AnalyzerHelper.SearchParameterOwner(
            AnalyzerHelper.GetSegmentFromOffset(context,
                context.GetOffset(pos.Line, pos.Character)) ?? context.TopSegment);

        IEnumerable<IParameterOwner>? parameterOwner = null;
        if (segment.SegmentType is SegmentType.VhdlFunction &&
            AnalyzerHelper.SearchFunctions(segment, segment.NameOrValue) is { } function)
        {
            parameterOwner = function;
        }
        else if (segment.SegmentType is SegmentType.NewFunction &&
                 AnalyzerHelper.SearchSeqFunction(segment, segment.LastName) is { } seqFunction)
        {
            parameterOwner = new[] { seqFunction };
        }
        else if (segment.SegmentType is SegmentType.CustomBuiltinFunction &&
                 CustomBuiltinFunction.DefaultBuiltinFunctions.ContainsKey(segment.NameOrValue.ToLower()))
        {
            parameterOwner = new[] { CustomBuiltinFunction.DefaultBuiltinFunctions[segment.NameOrValue.ToLower()] };
        }

        if (parameterOwner != null)
        {
            return new SignatureHelp
            {
                Signatures = new Container<SignatureInformation>(from parameters in parameterOwner
                    select new SignatureInformation
                    {
                        Parameters = new Container<ParameterInformation>(parameters.Parameters.Select(x =>
                            new ParameterInformation
                            {
                                Documentation = $"{x.Name} : {x.DataType}",
                                Label = new ParameterInformationLabel($"{x.Name} : {x.DataType}")
                            })),
                        Label = parameters.Name,
                        Documentation = parameters.Description
                    })
            };
        }

        if (segment.SegmentType is SegmentType.For)
        {
            if (AnalyzerHelper.SearchTopSegment(segment, SegmentType.Thread) is { })
            {
                return new SignatureHelp
                {
                    Signatures = new Container<SignatureInformation>(new SignatureInformation
                    {
                        Parameters = new Container<ParameterInformation>(new ParameterInformation
                        {
                            Label = new ParameterInformationLabel("VARIABLE i : INTEGER := 0; i < 10; i := i + 1")
                        }),
                        Label = "For",
                    })
                };
            }

            return new SignatureHelp
            {
                Signatures = new Container<SignatureInformation>(new SignatureInformation
                {
                    Parameters = new Container<ParameterInformation>(new ParameterInformation
                    {
                        Label = new ParameterInformationLabel("i in X to X")
                    }),
                    Label = "For",
                })
            };
        }

        return null;
    }

    public override async Task<CompletionList?> RequestCompletionAsync(string fullPath, Position pos,CompletionTriggerKind triggerKind, string? triggerChar)
    {
        await HdpProjectContext.AnalyzeAsync(fullPath, AnalyzerMode.Indexing | AnalyzerMode.Resolve);
        var context = HdpProjectContext.GetContext(fullPath);

        var offset = context.GetOffset(pos.Line, pos.Character);
        var segment =
            AnalyzerHelper.GetSegmentFromOffset(context, offset) ?? context.TopSegment;

        var items = new List<CompletionItem>();
        if (segment.Parent == null) return items;

        var inParameter = AnalyzerHelper.InParameter(segment);
        var inHeader = inParameter && AnalyzerHelper.SearchConcatParent(segment).Parent is
            { SegmentType: SegmentType.Main or SegmentType.Component };
        var useConcatParent = inParameter &&
                              AnalyzerHelper.SearchConcatParent(segment).Parent is
                                  { SegmentType: SegmentType.NewComponent } && segment.ConcatOperator is "," or null;

        var concatParent = AnalyzerHelper.SearchConcatParent(segment);
        if (useConcatParent && concatParent.Parent == null) return items;
        
        var validSegments = SegmentCheck
            .GetValidSegments(useConcatParent ? concatParent.Parent! : segment.Parent, inParameter).ToList();
        
        switch (triggerChar)
        {
            case ".":
                if (TypeCheck.ConvertTypeParameter(concatParent) is CustomDefinedRecord record)
                    AddVariables(items, record.Variables.Select(x => x.Value));
                break;

            default:
                switch (segment.ConcatOperator)
                {
                    case ":":
                    case "is" when segment.SegmentType is SegmentType.TypeUsage &&
                                   segment.Parent.SegmentType is SegmentType.SubType:
                        if (!inHeader)
                            AddTypes(items, context.AvailableTypes.Select(x => x.Value));
                        else
                            items.AddRange(
                                from varType in ParserHelper.VhdlIos //TODO only add in IO declaration segment
                                select new CompletionItem
                                {
                                    InsertText = varType.ToUpper(),
                                    Label = varType.ToUpper(),
                                    Kind = CompletionItemKind.Interface
                                });
                        break;
                    case "out":
                    case "in":
                    case "buffer":
                    case "inout":
                    case "return" when segment.Parent.Parent?.SegmentType is SegmentType.Function &&
                                       AnalyzerHelper.InParameter(segment):
                        AddTypes(items, context.AvailableTypes.Select(x => x.Value));
                        break;
                    default:
                        if (validSegments.Contains(SegmentType.DataVariable))
                        {
                            AddVariables(items, AnalyzerHelper.GetVariablesAtSegment(segment));
                            if (segment.SegmentType is SegmentType.DataVariable or SegmentType.NativeDataValue
                                or SegmentType.Unknown)
                                items.AddRange(from vD in ParserHelper.VhdlOperators
                                    select new CompletionItem
                                    {
                                        Label = vD.ToUpper(),
                                        InsertText = vD.ToUpper(),
                                        Documentation =
                                            new StringOrMarkupContent(SegmentInfo.GetInfoConcatMarkdown(vD)),
                                        Kind = CompletionItemKind.Operator
                                    });
                        }
                        else if (validSegments.Contains(SegmentType.ConnectionsMember))
                        {
                            AddVariables(items,
                                AnalyzerHelper.GetVariablesAtSegment(segment)
                                    .Where(x => x.VariableType is VariableType.Io));
                        }

                        if (validSegments.Contains(SegmentType.VariableDeclaration) && !(inParameter &&
                                segment.Parent.SegmentType is SegmentType.Main or SegmentType.Component
                                    or SegmentType.Package))
                        {
                            if (AnalyzerHelper.SearchTopSegment(segment, SegmentType.Process) != null)
                            {
                                items.Add(new CompletionItem
                                {
                                    Label = "VARIABLE",
                                    InsertText = "VARIABLE",
                                    Documentation =
                                        new StringOrMarkupContent(VariableTypeInfo.GetInfo(VariableType.Variable)),
                                    Kind = CompletionItemKind.Snippet
                                });
                            }

                            items.Add(new CompletionItem
                            {
                                Label = "CONSTANT",
                                InsertText = "CONSTANT",
                                Documentation =
                                    new StringOrMarkupContent(VariableTypeInfo.GetInfo(VariableType.Constant)),
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "SIGNAL",
                                InsertText = "SIGNAL",
                                Documentation =
                                    new StringOrMarkupContent(VariableTypeInfo.GetInfo(VariableType.Signal)),
                                Kind = CompletionItemKind.Snippet
                            });
                        }

                        if (validSegments.Contains(SegmentType.ConnectionsMember) && segment.ConcatOperator is "=>")
                        {
                            items.Clear();
                            items.Add(new CompletionItem
                            {
                                Label = "LED_?",
                                InsertText = "LED_",
                                Documentation = "Connect to LED (e.g LED_1)",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "BTN_?",
                                InsertText = "BTN_",
                                Documentation = "Connect to BTN (e.g BTN_1)",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "ADC_?",
                                InsertText = "ADC_",
                                Documentation = "Connect to ADC (e.g ADC_1)",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "FLASH_?",
                                InsertText = "FLASH_",
                                Documentation = "Connect to FLASH (e.g FLASH_1)",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "UART_RXD",
                                InsertText = "UART_RXD",
                                Documentation = "Connect to UART_RXD",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "UART_TXD",
                                InsertText = "UART_TXD",
                                Documentation = "Connect to UART_TXD",
                                Kind = CompletionItemKind.Snippet
                            });
                        }

                        if (validSegments.Contains(SegmentType.NewFunction))
                        {
                            items.AddRange(from comp in context.AvailableSeqFunctions
                                select new CompletionItem
                                {
                                    Label = "NewFunction " + comp.Value.Name,
                                    InsertText = $"NewFunction {comp.Value.Name} ($0);",
                                    Documentation = new StringOrMarkupContent(new MarkupContent
                                    {
                                        Kind = MarkupKind.Markdown,
                                        Value = FunctionInfo.GetInfoMarkdown(comp.Value),
                                    }),
                                    Kind = CompletionItemKind.Function
                                });
                        }

                        if (validSegments.Contains(SegmentType.CustomBuiltinFunction))
                        {
                            items.AddRange(from comp in CustomBuiltinFunction.DefaultBuiltinFunctions
                                select new CompletionItem
                                {
                                    Label = comp.Value.Name,
                                    InsertText = $"{comp.Value.Name}($0);",
                                    Documentation = new StringOrMarkupContent(new MarkupContent
                                    {
                                        Kind = MarkupKind.Markdown,
                                        Value = comp.Value?.Description ?? ""
                                    }),
                                    Kind = CompletionItemKind.Function
                                });
                        }

                        if (validSegments.Contains(SegmentType.NewComponent))
                        {
                            items.AddRange(from comp in context.AvailableComponents
                                select new CompletionItem
                                {
                                    Label = "New" + comp.Value.NameOrValue,
                                    InsertText = SegmentInfo.GetComponentInsert(comp.Value),
                                    Documentation = new StringOrMarkupContent(new MarkupContent
                                    {
                                        Kind = MarkupKind.Markdown,
                                        Value = SegmentInfo.GetComponentInfoMarkdown(comp.Value)
                                    }),
                                    Kind = CompletionItemKind.Module
                                });
                        }

                        if (validSegments.Contains(SegmentType.ComponentMember))
                        {
                            var component = concatParent.Parent;
                            context.AvailableComponents.TryGetValue(
                                component?.LastName?.ToLower() ?? "",
                                out var owner);

                            if (owner != null)
                            {
                                AddVariables(items,
                                    owner.Variables.Select(x => x.Value).Where(x =>
                                        x.VariableType is VariableType.Io or VariableType.Generic));
                            }
                        }

                        if (validSegments.Contains(SegmentType.Generate))
                        {
                            items.Add(new CompletionItem
                            {
                                Label = "Generate If",
                                InsertText = "Generate(if $0)\n{\n \n}",
                                Documentation =
                                    "Allows to generate a component or other operations if a condition is met",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "Generate For",
                                InsertText = "Generate(for ´i in 0 to 5´$0)\n{\n \n}",
                                Documentation = "Allows to generate a component or other operations multiple times",
                                Kind = CompletionItemKind.Snippet
                            });
                        }

                        if (validSegments.Contains(SegmentType.IncludePackage))
                        {
                            items.Add(new CompletionItem
                            {
                                Label = "ieee.numeric_std.all",
                                InsertText = "ieee.numeric_std.all",
                                Documentation = "ieee.numeric_std.all",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "ieee.std_logic_1164.all",
                                InsertText = "ieee.std_logic_1164.all",
                                Documentation = "ieee.std_logic_1164.all",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "ieee.math_real.all",
                                InsertText = "ieee.math_real.all",
                                Documentation = "ieee.math_real.all",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.AddRange(from package in segment.Context.AvailablePackages
                                select new CompletionItem
                                {
                                    Label = $"{package.Key}.all",
                                    InsertText = $"{package.Key}.all",
                                    Documentation = $"{package.Key}.all",
                                    Kind = CompletionItemKind.Snippet
                                });
                        }

                        if (validSegments.Contains(SegmentType.Type))
                        {
                            items.Add(new CompletionItem
                            {
                                Label = "type array",
                                InsertText = "type ´type_name´$0 is array (´range´) of ´element_type´;",
                                Documentation = "Array type declaration",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "type dynamic array",
                                InsertText = "type ´type_name´$0 is array (natural range <>) of ´element_type´;",
                                Documentation = "Dynamic array type declaration",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "type enum",
                                InsertText = "type ´type_name´$0 is ();",
                                Documentation = "Enum type declaration",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "type record",
                                InsertText = "type ´type_name´$0 is record\n    \nend record ´type_name´;",
                                Documentation = "Record type declaration",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "subtype",
                                InsertText = "subtype ´subtype_name´$0 is ´base_type´ range 0 to 7;",
                                Documentation = "Subtype declaration",
                                Kind = CompletionItemKind.Snippet
                            });
                        }

                        if (validSegments.Contains(SegmentType.VhdlFunctionReturn))
                        {
                            if (AnalyzerHelper.InParameter(segment) &&
                                segment.Parent.SegmentType is SegmentType.Function)
                            {
                                items.Add(new CompletionItem
                                {
                                    Label = "return",
                                    InsertText = "return ´type´",
                                    Documentation = "Specify return type for function",
                                    Kind = CompletionItemKind.Snippet
                                });
                            }
                            else
                            {
                                items.Add(new CompletionItem
                                {
                                    Label = "return",
                                    InsertText = "return",
                                    Documentation = "Specify return value",
                                    Kind = CompletionItemKind.Snippet
                                });
                            }
                        }

                        if (validSegments.Contains(SegmentType.VhdlFunction))
                        {
                            foreach (var func in context.AvailableFunctions)
                            {
                                items.AddRange(from vhdlFunction in func.Value
                                    select new CompletionItem
                                    {
                                        Label = $"{vhdlFunction.Name} {ParameterShort(vhdlFunction.Parameters)}",
                                        InsertText = FunctionInfo.GetInsert(vhdlFunction),
                                        Documentation = FunctionInfo.GetInfoMarkdown(vhdlFunction),
                                        Kind = CompletionItemKind.Function
                                    });
                            }

                            //Converters
                            items.Add(new CompletionItem
                            {
                                Label = "STD_LOGIC_VECTOR (INTEGER)",
                                InsertText = "STD_LOGIC_VECTOR(TO_SIGNED(´integer´$0, ´std_logic_vector´'LENGTH))",
                                Documentation = "Converts Integer to STD_LOGIC_VECTOR",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "STD_LOGIC_VECTOR (NATURAL)",
                                InsertText = "STD_LOGIC_VECTOR(TO_UNSIGNED(´natural´$0, ´std_logic_vector´'LENGTH))",
                                Documentation = "Converts Natural to STD_LOGIC_VECTOR",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "resize (STD_LOGIC_VECTOR)",
                                InsertText = "std_logic_vector(resize(unsigned(´std_logic_vector´$0), ´new_length´))",
                                Documentation = "Changes length of STD_LOGIC_VECTOR",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "INTEGER (STD_LOGIC_VECTOR)",
                                InsertText = "TO_INTEGER(SIGNED(´std_logic_vector´$0))",
                                Documentation = "Converts Integer to STD_LOGIC_VECTOR",
                                Kind = CompletionItemKind.Snippet
                            });
                            items.Add(new CompletionItem
                            {
                                Label = "NATURAL (STD_LOGIC_VECTOR)",
                                InsertText = "TO_INTEGER(UNSIGNED(´std_logic_vector´$0))",
                                Documentation = "Converts Natural to STD_LOGIC_VECTOR",
                                Kind = CompletionItemKind.Snippet
                            });
                        }

                        items.AddRange(from sType in validSegments
                            where sType is not (SegmentType.DataVariable or SegmentType.NativeDataValue
                                or SegmentType.VariableDeclaration or SegmentType.EmptyName
                                or SegmentType.VhdlFunction or SegmentType.CustomBuiltinFunction or SegmentType.VhdlEnd
                                or SegmentType.TypeUsage or SegmentType.EnumDeclaration or SegmentType.ConnectionsMember
                                or SegmentType.VhdlAttribute or SegmentType.ComponentMember
                                or SegmentType.IncludePackage or SegmentType.VhdlFunctionReturn)
                            select new CompletionItem
                            {
                                Label = sType.ToString(),
                                InsertText = SegmentInfo.GetSnippet(sType, inParameter),
                                Documentation = new StringOrMarkupContent(SegmentInfo.GetInfo(sType)),
                                Kind = CompletionItemKind.Snippet
                            });
                        break;
                }

                break;
        }

        return items.Count > 0 ? new CompletionList(items) : null;
    }

    public override Task<TextEditContainer?> RequestFormattingAsync(string fullPath)
    {
        var context = HdpProjectContext.GetContext(fullPath);
        
        var items = new List<TextEdit>
        {
            new TextEdit()
        };

        var eC = new TextEditContainer();
        return Task.FromResult<TextEditContainer?>(new TextEditContainer(items));
    }

    private static string ParameterShort(List<FunctionParameter> parameters)
    {
        return parameters.Count switch
        {
            1 => $"({parameters[0].DataType})",
            2 => $"({parameters[0].DataType}, {parameters[1].DataType})",
            > 2 => "(..)",
            _ => ""
        };
    }

    private static void AddTypes(List<CompletionItem> items, IEnumerable<DataType> customTypes)
    {
        items.AddRange(from type in customTypes
            select new CompletionItem
            {
                InsertText = type.Name,
                Label = type.Name,
                Documentation = new StringOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = $"```vhdp\n{type.Description}\n```",
                }),
                Kind = CompletionItemKind.Class
            });
    }

    private static void AddVariables(List<CompletionItem> items, IEnumerable<DefinedVariable> variables)
    {
        //Add available variables at position
        items.AddRange(from variable in variables
            let kind = variable.VariableType switch
            {
                VariableType.Constant => CompletionItemKind.Constant,
                VariableType.Io => CompletionItemKind.Interface,
                _ => CompletionItemKind.Variable,
            }
            select new CompletionItem
            {
                InsertText = variable.Name,
                Label = variable.Name,
                Documentation = new StringOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = $"```vhdp\n{variable}\n```",
                }),
                Kind = kind,
            });
    }

    public override Task<IEnumerable<LocationOrLocationLink>?> RequestDefinitionAsync(string fullPath, Position pos)
    {
        var context = HdpProjectContext.GetContext(fullPath);
        var segment = AnalyzerHelper.GetSegmentFromOffset(context,
            context.GetOffset(pos.Line, pos.Character));

        if (segment == null) return Task.FromResult<IEnumerable<LocationOrLocationLink>?>(null);
        switch (segment.SegmentType)
        {
            case SegmentType.DataVariable:
            {
                var variable = AnalyzerHelper.SearchVariable(segment, segment.NameOrValue);
                if (variable?.Owner is { } definition)
                {
                    return Task.FromResult<IEnumerable<LocationOrLocationLink>?>(new[]
                        { SegmentToLocation(definition) });
                }

                break;
            }
            case SegmentType.NewComponent:
            {
                if (segment.Context.AvailableComponents.ContainsKey(segment.LastName.ToLower()))
                {
                    var definition = segment.Context.AvailableComponents[segment.LastName.ToLower()];
                    return Task.FromResult<IEnumerable<LocationOrLocationLink>?>(new[]
                        { SegmentToLocation(definition) });
                }

                break;
            }
            case SegmentType.ComponentMember:
            {
                var comp = AnalyzerHelper.SearchConcatParent(segment).Parent;
                if (comp is { SegmentType: SegmentType.NewComponent } owner &&
                    segment.Context.AvailableComponents.ContainsKey(owner.LastName.ToLower()))
                {
                    var definition = segment.Context.AvailableComponents[owner.LastName.ToLower()];
                    if (definition.Variables.ContainsKey(segment.NameOrValue.ToLower()))
                    {
                        return Task.FromResult<IEnumerable<LocationOrLocationLink>?>(new[]
                            { SegmentToLocation(definition.Variables[segment.NameOrValue.ToLower()].Owner) });
                    }
                }

                break;
            }
            case SegmentType.NewFunction:
            {
                if (segment.Context.AvailableSeqFunctions.ContainsKey(segment.LastName.ToLower()))
                {
                    var definition = segment.Context.AvailableSeqFunctions[segment.LastName.ToLower()];
                    return Task.FromResult<IEnumerable<LocationOrLocationLink>?>(new[]
                        { SegmentToLocation(definition.Owner) });
                }

                break;
            }
            case SegmentType.VhdlFunction:
            {
                if (segment.Context.AvailableFunctions.ContainsKey(segment.LastName.ToLower()))
                {
                    var definition = segment.Context.AvailableFunctions[segment.LastName.ToLower()].FirstOrDefault();
                    if (definition is { Owner: not null })
                        return Task.FromResult<IEnumerable<LocationOrLocationLink>?>(new[]
                            { SegmentToLocation(definition.Owner) });
                }

                break;
            }
            case SegmentType.TypeUsage:
            {
                if (segment.Context.AvailableTypes.ContainsKey(segment.NameOrValue.ToLower()))
                {
                    var definition = segment.Context.AvailableTypes[segment.NameOrValue.ToLower()];
                    if (definition.Owner != null)
                        return Task.FromResult<IEnumerable<LocationOrLocationLink>?>(new[]
                            { SegmentToLocation(definition.Owner) });
                }

                break;
            }
        }

        return Task.FromResult<IEnumerable<LocationOrLocationLink>?>(null);
    }

    private LocationOrLocationLink SegmentToLocation(Segment definition)
    {
        var startLine = definition.Context.GetLine(definition.Offset);
        var startCol = definition.Context.GetCol(definition.Offset);
        var endLine = definition.Context.GetLine(definition.Offset + definition.NameOrValue.Length);
        var endCol = definition.Context.GetCol(definition.Offset + definition.NameOrValue.Length);
        return new(new Location
        {
            Range = new Range(startLine, startCol, endLine, endCol),
            Uri = DocumentUri.FromFileSystemPath(definition.Context.FilePath)
        });
    }

    public override IEnumerable<string> GetCompletionTriggerChars()
    {
        return ["."];
    }

    public override IEnumerable<string> GetSignatureHelpTriggerChars()
    {
        return ["("];
    }

    public override IEnumerable<string> GetSignatureHelpRetriggerChars()
    {
        return [","];
    }
}