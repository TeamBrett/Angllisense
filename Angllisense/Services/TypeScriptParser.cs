using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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

    public enum AccessModifier {
        Public,
        Private,
        Protected,
    }

    public class TypeScriptCodeModel {
        public ICollection<TypeScriptParser.QuotedString> QuotedStrings { get; set; } = new List<TypeScriptParser.QuotedString>();
        public ICollection<Module> Modules { get; set; } = new List<Module>();
    }

    public class Module {
        public string Name { get; set; }
        public List<Module> Modules { get; set; } = new List<Module>();
        public ICollection<Class> Classes { get; set; } = new List<Class>();
    }

    public class Class {
        public string Name { get; set; }
        public ICollection<Field> Fields { get; set; } = new List<Field>();
        public ICollection<Function> Functions { get; set; } = new List<Function>();
        public bool IsExported { get; set; }
    }

    public class Attribute {
        public string Name { get; set; }
        public AccessModifier AccessModifier;
    }

    public class Field : Attribute {
        public Types Types { get; set; }
        public Class ObjectType { get; set; }
    }

    public class Function : Attribute {
        public Types ReturnTypes { get; set; }

        public IOrderedEnumerable<Argument> OrderedEnumerable { get; set; }
    }

    public class Argument {
        public string Name { get; set; }
        public Types Types { get; set; }
        public Class ObjectType { get; set; }
    }

    public class TypeScriptParser {
        private IList<string> tokens;
        private int i;
        private TypeScriptCodeModel model = new TypeScriptCodeModel();

        public TypeScriptCodeModel Parse(string code) {

            var quoteResult = ReplaceQuotedStrings(code);
            this.model.QuotedStrings = quoteResult.Item2;
            code = quoteResult.Item1;

            code = BreakOutCompressedCharacters(code);

            code = RemoveRedundantWhiteSpace(code);

            this.tokens = code.Split(' ').Where(token => !string.IsNullOrWhiteSpace(token)).ToList();

            this.model.Modules = this.GetModules();

            return this.model;
        }

        public ICollection<Module> GetModules(Module root = null) {
            root = root ?? new Module();
            while (this.i < this.tokens.Count) {
                if (this.HasOptional(Tokens.EndCurly)) {
                    break; // end of module
                }

                this.AssertCurrentIs(Tokens.Module);

                var module = this.GetModule(root);

                this.AssertCurrentIs(Tokens.StartCurly);

                if (this.HasOptional(Tokens.EndCurly)) {
                    continue; // end of module
                }

                if (this.NextIs(Tokens.Module)) {
                    // nested module
                    module.Modules.AddRange(this.GetModules(root));
                    if (this.HasOptional(Tokens.EndCurly)) {
                        continue; // end of module
                    }
                }

                if (this.NextIsClass()) {
                    module.Classes = this.GetClasses();
                }

                if (this.HasOptional(Tokens.EndCurly)) {
                    continue; // end of module
                }
            }

            return root.Modules;
        }

        private Module GetModule(Module root) {
            string @namespace = this.Pop();
            var namespaces = @namespace.Split('.');
            var module = root;
            foreach (string piece in namespaces) {
                var nextModule = module.Modules.FirstOrDefault(x => x.Name == piece);
                if (nextModule != null) {
                    module = nextModule;
                    continue;
                }

                nextModule = new Module { Name = piece };
                module.Modules.Add(nextModule);
                module = nextModule;
            }

            return module;
        }

        private bool NextIsClass() {
            return this.NextIs(Tokens.Class) || (this.NextIs(Tokens.Export) && this.tokens[this.i + 1] == Tokens.Class);
        }

        public ICollection<Class> GetClasses() {
            var classes = new List<Class>();
            while(this.i < this.tokens.Count) {
                if (this.CurrentIs(Tokens.EndCurly)) {
                    break; // end of module, save token for module processor
                }

                var @class = new Class();

                @class.IsExported = this.HasOptional(Tokens.Export);

                this.AssertCurrentIs(Tokens.Class);

                @class.Name = this.Pop();

                classes.Add(@class);

                this.AssertCurrentIs(Tokens.StartCurly);

                if (this.HasOptional(Tokens.EndCurly)) {
                    continue; // end of class
                }

                @class.Fields = this.GetFields();
                @class.Functions = this.GetFunctions();

                this.AssertCurrentIs(Tokens.EndCurly); // end of class
            }

            return classes;
        }

        public AccessModifier GetAccessModifier() {
            if (this.HasOptional(Tokens.Public)) {
                return AccessModifier.Public;
            }

            if (this.HasOptional(Tokens.Private)) {
                return AccessModifier.Private;
            }

            if (this.HasOptional(Tokens.Protected)) {
                return AccessModifier.Protected;
            }

            return AccessModifier.Public;
        }

        public Types GetTypes(string type) {
            if (type == "string") {
                return Types.String;
            }

            return Types.Any;
        }

        public ICollection<Function> GetFunctions() {
            return new List<Function>();
        }

        public ICollection<Field> GetFields() {
            var field = new Field();
            field.AccessModifier = this.GetAccessModifier();
            var name = this.Pop();
            string type = string.Empty;
            string value = string.Empty;

            if (name.Contains(":")) {
                var nameType = name.Split(':');
                if (nameType.Length > 2) {
                    throw new Exception($"Do not know how to handle \"{name}\"");
                }

                name = nameType[0];
                type = nameType[1];
            }

            if (this.HasOptional("=")) {
                value = this.GetName(this.Pop());
            }

            if (type.Contains("=")) {
                var assignment = type.Split('=');
                type = assignment[0];
            }

            this.HasOptional(Tokens.EndStatement);

            field.Name = this.GetName(name);
            field.Types = this.GetTypes(type);

            return new List<Field>();
        }

        private string GetName(string name) {
            var quotedName = this.model.QuotedStrings.FirstOrDefault(x => x.Id == name);
            if (quotedName != null) {
                return quotedName.Value;
            }

            return name;
        }

        private bool HasOptional(string token) {
            var next = this.Peek();
            if (next != token) {
                return false;
            }

            this.DiscardCurrent();
            return true;
        }

        private string Peek() {
            if (this.tokens.Count == this.i) {
                return string.Empty;
            }

            return this.tokens[this.i];
        }

        private bool NextIs(string expected) {
            return this.Peek() == expected;
        }

        private void AssertCurrentIs(string expected) {
            var actual = this.Pop();
            if (actual != expected) {
                var restOfCode = actual;
                for (; this.i < this.tokens.Count; this.i++) {
                    restOfCode += " " + this.tokens[this.i];
                }
                throw new Exception("Expected \"" + expected + "\".  Got \"" + restOfCode + "\"");
            }
        }

        private bool CurrentIs(string token) {
            return this.Peek() == token;
        }

        private void DiscardCurrent() {
            this.i++;
        }

        private string Pop() {
            if (this.tokens.Count == this.i) {
                return this.tokens[this.i - 1];
            }

            return this.tokens[this.i++];
        }

        public static string BreakOutCompressedCharacters(string code) {
            code = Regex.Replace(code, @"\s*[}]\s*", " } ");
            code = Regex.Replace(code, @"\s*[{]\s*", " { ");
            code = Regex.Replace(code, @"\s*[;]\s*", " ; ");
            return code;
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

                var id = string.Format(stringIdentifierFormat, stringIdentifier, quotedStrings.Count);

                var quotedString = new QuotedString {
                    Id = id,
                    Value = code.Substring(startIndex + 1, i - startIndex - 1)
                };

                quotedStrings.Add(quotedString);

                builder.Append(id);

                startQuote = null;
                startIndex = i + 1;
            }

            builder.Append(code.Substring(startIndex));

            return new Tuple<string, List<QuotedString>>(builder.ToString(), quotedStrings);
        }

        private static class Tokens {
            public const string StartCurly = "{";
            public const string EndCurly = "}";

            public const string GenericStart = "<";
            public const string GenericEnd = ">";

            public const string ArrayStart = "[";
            public const string ArrayEnd = "]";

            public const string EndStatement = ";";
            public const string Assignment = "=";
            public const string Truthy = "==";
            public const string Equal = "===";
            public const string Falsy = "!=";
            public const string NotEqual = "!==";
            public const string LessThan = "<";
            public const string LessOrEQual = "<";
            public const string GreaterThan = ">";
            public const string GreaterOrEqual = ">=";
            public const string Multiply = "*";
            public const string Divide = "/";

            public const string Module = "module";
            public const string Class = "class";
            public const string Export = "export";
            public const string Extends = "export";
            public const string Implements = "export";
            public const string Public = "public";
            public const string Private = "private";
            public const string Protected = "protected";
            public const string Abstract = "abstract";
            public const string Static = "static";
        }

        public class QuotedString {
            public string Id { get; set; }
            public string Value { get; set; }
        }
    }
}
