using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Configuration;
using System.Text;

namespace Angllisense.Services {
    [Flags]
    public enum Types {
        Any,
        Number,
        String,
        Boolean,
        Void,
        Null,
        Undefined,

        Array,
        Enum,
        Object,
        Function
    }

    public class TypeScriptCodeModel {
        public ICollection<TypeScriptParser.QuotedString> QuotedStrings { get; set; } = new List<TypeScriptParser.QuotedString>();
        public ICollection<Module> Modules { get; set; } = new List<Module>();
    }

    public class Module {
        public string Name { get; set; }
        public ICollection<Module> Modules { get; set; } = new List<Module>();
        public ICollection<Class> Classes { get; set; } = new List<Class>();
    }

    public class Class {
        public string Name { get; set; }
        public ICollection<Field> Fields { get; set; } = new List<Field>();
        public ICollection<Function> Functions { get; set; } = new List<Function>();
        public bool IsExported { get; set; }
    }

    public class Field {
        public string Name { get; set; }
        public Types Types { get; set; }
        public Class ObjectType { get; set; }
    }

    public class Function {
        public string Name { get; set; }
        public Types ReturnTypes { get; set; }

        public IOrderedEnumerable<Argument> OrderedEnumerable { get; set; }
    }

    public class Argument {
        public string Name { get; set; }
        public Types Types { get; set; }
        public Class ObjectType { get; set; }
    }

    public static class TypeScriptParser {
        public static TypeScriptCodeModel Parse(string code) {
            var model = new TypeScriptCodeModel();

            var quoteResult = ReplaceQuotedStrings(code);
            model.QuotedStrings = quoteResult.Item2;
            code = quoteResult.Item1;

            code = RemoveRedundantWhiteSpace(code);

            var tokens = code.Split(' ').Where(token => !string.IsNullOrWhiteSpace(token)).ToList();

            model.Modules = GetModules(tokens);

            return model;
        }

        public static ICollection<Module> GetModules(IList<string> tokens) {
            var root = new Module();
            int i = 0;
            while (i < tokens.Count) {
                var token = tokens[i++];

                if (token.ToLower() == "module") {
                    var @namespace = tokens[i++];

                    var namespacePieces = @namespace.Split('.');
                    Module module = root;
                    foreach (var piece in namespacePieces) {
                        var nextModule = module.Modules.FirstOrDefault(x => x.Name == piece);
                        if (nextModule != null) {
                            module = nextModule;
                            continue;
                        }

                        nextModule = new Module { Name = piece };
                        module.Modules.Add(nextModule);
                        module = nextModule;
                    }

                    token = tokens[i++];
                    if (token != "{") {
                        throw new Exception("Expected {.  Got " + token);
                    }

                    token = Peek(tokens, i);
                    if (token == "}") {
                        Pop(tokens, ref i);
                        continue;
                    }

                    module.Classes = GetClasses(tokens, ref i);

                    token = tokens[i++];
                    if (token != "}") {
                        throw new Exception("Expected }.  Got " + token);
                    }
                } else {
                    throw new Exception("Expected module.  Got " + token);
                }
            }

            return root.Modules;
        }

        private static string Peek(IList<string> tokens, int i) {
            return tokens[i];
        }

        private static string Pop(IList<string> tokens, ref int i) {
            return tokens[i++];
        }

        public static ICollection<Class> GetClasses(IList<string> tokens, ref int i) {
            var classes = new List<Class>();
            while(i < tokens.Count) {
                var token = Peek(tokens, i);
                if (token == "}") {
                    break;
                }

                token = Pop(tokens, ref i);

                var @class = new Class();
                if (token == "export") {
                    @class.IsExported = true;
                    token = Pop(tokens, ref i);
                }

                if (token.ToLower() != "class") {
                    throw new Exception("Expected class.  Got " + token);
                }

                @class.Name = tokens[i++];
                classes.Add(@class);

                token = tokens[i++];
                if (token != "{") {
                    throw new Exception("Expected {.  Got " + token);
                }

                token = Peek(tokens, i);
                if (token == "}") {
                    Pop(tokens, ref i);
                    continue;
                }

                @class.Fields = GetFields(tokens, i);

                token = tokens[i++];
                if (token != "}") {
                    throw new Exception("Expected }.  Got " + token);
                }
            }

            return classes;
        }

        public static ICollection<Field> GetFields(IList<string> tokens, int i) {
            return new List<Field>();
        } 

        public static  string RemoveRedundantWhiteSpace(string code) {
            var buffer = new char[code.Length];
            bool lastCharIsSpace = false;
            var j = 0;
            for (var i = 0; i < code.Length; i++) {
                char c = code[i];
                if (char.IsWhiteSpace(c)) {
                    if (lastCharIsSpace) {
                        continue;
                    }

                    c = ' ';
                    lastCharIsSpace = true;
                } else {
                    lastCharIsSpace = false;
                }

                buffer[j++] = c;
            }

            return new string(buffer).Trim('\0');
        }

        public static Tuple<string, List<QuotedString>> ReplaceQuotedStrings(string code) {
            var stringIdentifier = "______";
            var stringIdentifierFormat = "{0}{1}{0}";

            while (code.Contains(stringIdentifier)) {
                stringIdentifier += "_";
            }

            var quotedStrings = new List<QuotedString>();
            char? startQuote = null;
            int startIndex = 0;
            var builder = new StringBuilder();
            for (int i = 0; i < code.Length; i++) {
                var c = code[i];
                char quote;
                if (c == '"') {
                    quote = '"';
                } else if (c == '\'') {
                    quote = '\'';
                } else if (c == '`') {
                    quote = '`';
                } else {
                    continue;
                }

                if (!startQuote.HasValue) {
                    startQuote = quote;
                    builder.Append(code.Substring(startIndex, i));
                    startIndex = i;
                    continue;
                }

                var quotedString = new QuotedString {
                    Id = quotedStrings.Count,
                    Value = code.Substring(startIndex + 1, i - startIndex - 1)
                };

                quotedStrings.Add(quotedString);

                builder.Append(string.Format(stringIdentifierFormat, stringIdentifier, quotedString.Id));

                startQuote = null;
                startIndex = i + 1;
            }

            builder.Append(code.Substring(startIndex));

            return new Tuple<string, List<QuotedString>>(builder.ToString(), quotedStrings);
        }

        public class QuotedString {
            public int Id { get; set; }
            public string Value { get; set; }
        }
    }
}
