using System;
using System.Linq;

using Angllisense.Services;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests {
    [TestClass]
    public class UnitTest1 {
        [TestMethod]
        public void ReplaceQuotedStrings_HandlesQuotes() {
            const string Input = "word1 word 2 \"Hello World\" word3 word4";
            const string Expected = "word1 word 2 ______0______ word3 word4";
            var actual = TypeScriptParser.ReplaceQuotedStrings(Input);
            foreach (var quotedString in actual.Item2) {
                Console.WriteLine(quotedString.Value);
            }

            Assert.AreEqual(Expected, actual.Item1);
            Assert.AreEqual("Hello World", actual.Item2.First().Value);
        }

        [TestMethod]
        public void ReplaceQuotedStrings_HandlesTicks() {
            const string Input = "word1 word 2 'Hello World' word3 word4";
            const string Expected = "word1 word 2 ______0______ word3 word4";
            var actual = TypeScriptParser.ReplaceQuotedStrings(Input);
            Assert.AreEqual(Expected, actual.Item1);
            Assert.AreEqual("Hello World", actual.Item2.First().Value);
        }

        [TestMethod]
        public void ReplaceQuotedStrings_HandlesBackTicks() {
            const string Input = "word1 word 2 `Hello World` word3 word4";
            const string Expected = "word1 word 2 ______0______ word3 word4";
            var actual = TypeScriptParser.ReplaceQuotedStrings(Input);
            Assert.AreEqual(Expected, actual.Item1);
            Assert.AreEqual("Hello World", actual.Item2.First().Value);
        }

        [TestMethod]
        public void RemoveLineReturns_HandlesWindowsFormat() {
            const string Input = "word1 word \r\n 2 `Hello World` word3 word4";
            const string Expected = "word1 word 2 `Hello World` word3 word4";
            var actual = TypeScriptParser.RemoveRedundantWhiteSpace(Input);
            Assert.AreEqual(Expected, actual);
        }

        [TestMethod]
        public void RemoveLineReturns_HandlesWindowsUnix() {
            const string Input = "word1 word \n 2 `Hello World` word3 word4";
            const string Expected = "word1 word 2 `Hello World` word3 word4";
            var actual = TypeScriptParser.RemoveRedundantWhiteSpace(Input);
            Assert.AreEqual(Expected, actual);
        }

        [TestMethod]
        public void RemoveLineReturns_HandlesBoth() {
            const string Input = "module test { class myClass { public testString = \n `Hello \r\nWorld`;}}";
            const string Expected = "module test { class myClass { public testString = `Hello World`;}}";
            var actual = TypeScriptParser.RemoveRedundantWhiteSpace(Input);
            Assert.AreEqual(Expected, actual);
        }

        [TestMethod]
        public void Parse_QuotedLineReturns_PreservesLineReturns() {
            const string Input = "module test { class myClass { public testString = \n `Hello \r\nWorld`;}}";
            var model = TypeScriptParser.Parse(Input);
            Assert.AreEqual("Hello \r\nWorld", model.QuotedStrings.First().Value);
        }

        [TestMethod]
        public void RemoveRedundantWhiteSpace_RemovesTabs() {
            const string Input = "word1 word 2 `Hello \r\nWorld` word3 \t word4";
            const string Expected = "word1 word 2 `Hello World` word3 word4";
            var actual = TypeScriptParser.RemoveRedundantWhiteSpace(Input);
            Assert.AreEqual(Expected, actual);
        }

        [TestMethod]
        public void Parse_ReturnsMultipleModules() {
            const string Input = @"
module Flemco.Test1 {
}

module Flemco.Test2 {
}
";
            var model = TypeScriptParser.Parse(Input);
            Assert.AreEqual(1, model.Modules.Count);
            var module = model.Modules.FirstOrDefault();

            Assert.IsNotNull(module);
            Assert.AreEqual("Flemco", module.Name);
            Assert.AreEqual(2, module.Modules.Count);
            Assert.AreEqual("Test1", module.Modules.First().Name);
            Assert.AreEqual("Test2", module.Modules.Second().Name);
        }

        [TestMethod]
        public void Parse_ReturnsMultipleClasses() {
            const string Input = @"
module Flemco {
    export class Class1 {
    }

    class Class2 {
    }
}
";
            var actual = TypeScriptParser.RemoveRedundantWhiteSpace(Input);
            var model = TypeScriptParser.Parse(Input);
            Assert.AreEqual(1, model.Modules.Count);
            var module = model.Modules.FirstOrDefault();
            Assert.AreEqual(2, module.Classes.Count);
            Assert.AreEqual("Class1", module.Classes.First().Name);
            Assert.AreEqual("Class2", module.Classes.Second().Name);
        }

        [TestMethod]
        public void Parse_ParsesFields() {
            const string Input = @"
module Flemco.Test {
    export class Class1 {
        public Field1: string;
        Field2: any;
        private Field3;
    }
}
";
            var actual = TypeScriptParser.RemoveRedundantWhiteSpace(Input);

        }

        [TestMethod]
        public void Parse_GivesProperModel() {
            const string Input = @"
module Flemco.Test {
    export class Class1 {
        public Field1: string;
        Field2: any;
        private Field3;
    }

    class Class1 {
        public Field1: string;
        Field2: Array<any>;
        private Field3: [];
    }
}
";
            var actual = TypeScriptParser.RemoveRedundantWhiteSpace(Input);

        }
    }
}
