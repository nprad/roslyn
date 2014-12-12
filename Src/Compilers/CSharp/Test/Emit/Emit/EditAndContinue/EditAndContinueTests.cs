﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.
#pragma warning disable 618
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using Microsoft.CodeAnalysis.CodeGen;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.CSharp.UnitTests;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.MetadataUtilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.EditAndContinue.UnitTests
{
    public class EditAndContinueTests : EditAndContinueTestBase
    {
        [Fact]
        public void DeltaHeapsStartWithEmptyItem()
        {
            var source0 =
@"class C
{
    static string F() { return null; }
}";
            var source1 =
@"class C
{
    static string F() { return ""a""; }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);

            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;

                var method0 = compilation0.GetMember<MethodSymbol>("C.F");
                var method1 = compilation1.GetMember<MethodSymbol>("C.F");

                var diff1 = compilation1.EmitDifference(
                    EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider),
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1)));

                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;

                    var s = MetadataTokens.StringHandle(0);
                    Assert.Equal(reader1.GetString(s), "");

                    var b = MetadataTokens.BlobHandle(0);
                    Assert.Equal(0, reader1.GetBlobBytes(b).Length);

                    var us = MetadataTokens.UserStringHandle(0);
                    Assert.Equal(reader1.GetUserString(us), "");
                }
            }
        }

        [Fact]
        public void ModifyMethod()
        {
            var source0 =
@"class C
{
    static void Main() { }
    static string F() { return null; }
}";
            var source1 =
@"class C
{
    static void Main() { }
    static string F() { return string.Empty; }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugExe);
            var compilation1 = compilation0.WithSource(source1);

            // Verify full metadata contains expected rows.
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "C");
                CheckNames(reader0, reader0.GetMethodDefNames(), "Main", "F", ".ctor");
                CheckNames(reader0, reader0.GetMemberRefNames(), /*CompilationRelaxationsAttribute.*/".ctor", /*RuntimeCompatibilityAttribute.*/".ctor", /*Object.*/".ctor", /*DebuggableAttribute*/".ctor");

                var method0 = compilation0.GetMember<MethodSymbol>("C.F");
                var generation0 = EmitBaseline.CreateInitialBaseline(
                    md0,
                    EmptyLocalsProvider);
                var method1 = compilation1.GetMember<MethodSymbol>("C.F");

                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1)));

                // Verify delta metadata contains expected rows.
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    EncValidation.VerifyModuleMvid(1, reader0, reader1);
                    CheckNames(readers, reader1.GetTypeDefNames());
                    CheckNames(readers, reader1.GetMethodDefNames(), "F");
                    CheckNames(readers, reader1.GetMemberRefNames(), /*String.*/"Empty");
                    CheckEncLog(reader1,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(5, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(6, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(7, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.StandAloneSig, EditAndContinueOperation.Default),
                        Row(2, TableIndex.MethodDef, EditAndContinueOperation.Default)); // C.F
                    CheckEncMap(reader1,
                        Handle(6, TableIndex.TypeRef),
                        Handle(7, TableIndex.TypeRef),
                        Handle(2, TableIndex.MethodDef),
                        Handle(5, TableIndex.MemberRef),
                        Handle(2, TableIndex.StandAloneSig),
                        Handle(2, TableIndex.AssemblyRef));
                }
            }
        }

        [WorkItem(962219)]
        [Fact]
        public void PartialMethod()
        {
            var source =
@"partial class C
{
    static partial void M1();
    static partial void M2();
    static partial void M3();
    static partial void M1() { }
    static partial void M2() { }
}";
            var compilation0 = CreateCompilationWithMscorlib(source, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source);

            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetMethodDefNames(), "M1", "M2", ".ctor");

                var method0 = compilation0.GetMember<MethodSymbol>("C.M2").PartialImplementationPart;
                var method1 = compilation1.GetMember<MethodSymbol>("C.M2").PartialImplementationPart;
                var generation0 = EmitBaseline.CreateInitialBaseline(
                    md0,
                    EmptyLocalsProvider);
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1)));

                var methods = diff1.TestData.Methods;
                Assert.Equal(methods.Count, 1);
                Assert.True(methods.ContainsKey("C.M2()"));

               using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    EncValidation.VerifyModuleMvid(1, reader0, reader1);
                    CheckNames(readers, reader1.GetMethodDefNames(), "M2");
                    CheckEncLog(reader1,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(6, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.MethodDef, EditAndContinueOperation.Default));
                    CheckEncMap(reader1,
                        Handle(6, TableIndex.TypeRef),
                        Handle(2, TableIndex.MethodDef),
                        Handle(2, TableIndex.AssemblyRef));
                }
            }
        }

        /// <summary>
        /// Add a method that requires entries in the ParameterDefs table.
        /// Specifically, normal parameters or return types with attributes.
        /// Add the method in the first edit, then modify the method in the second.
        /// </summary>
        [Fact]
        public void AddThenModifyMethod()
        {
            var source0 =
@"class A : System.Attribute { }
class C
{
    static void Main() { F1(null); }
    static object F1(string s1) { return s1; }
}";
            var source1 =
@"class A : System.Attribute { }
class C
{
    static void Main() { F2(); }
    [return:A]static object F2(string s2 = ""2"") { return s2; }
}";
            var source2 =
@"class A : System.Attribute { }
class C
{
    static void Main() { F2(); }
    [return:A]static object F2(string s2 = ""2"") { return null; }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugExe);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation0.WithSource(source2);

            // Verify full metadata contains expected rows.
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "A", "C");
                CheckNames(reader0, reader0.GetMethodDefNames(), ".ctor", "Main", "F1", ".ctor");
                CheckNames(reader0, reader0.GetParameterDefNames(), "s1");

                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);

                var method1 = compilation1.GetMember<MethodSymbol>("C.F2");
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Insert, null, method1)));

                // Verify delta metadata contains expected rows.
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    EncValidation.VerifyModuleMvid(1, reader0, reader1);
                    CheckNames(readers, reader1.GetTypeDefNames());
                    CheckNames(readers, reader1.GetMethodDefNames(), "F2");
                    CheckNames(readers, reader1.GetParameterDefNames(), "", "s2");
                    CheckEncLog(reader1,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(7, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.StandAloneSig, EditAndContinueOperation.Default),
                        Row(3, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(5, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(5, TableIndex.MethodDef, EditAndContinueOperation.AddParameter),
                        Row(2, TableIndex.Param, EditAndContinueOperation.Default),
                        Row(5, TableIndex.MethodDef, EditAndContinueOperation.AddParameter),
                        Row(3, TableIndex.Param, EditAndContinueOperation.Default),
                        Row(1, TableIndex.Constant, EditAndContinueOperation.Default),
                        Row(4, TableIndex.CustomAttribute, EditAndContinueOperation.Default));
                    CheckEncMap(reader1,
                        Handle(7, TableIndex.TypeRef),
                        Handle(5, TableIndex.MethodDef),
                        Handle(2, TableIndex.Param),
                        Handle(3, TableIndex.Param),
                        Handle(1, TableIndex.Constant),
                        Handle(4, TableIndex.CustomAttribute),
                        Handle(2, TableIndex.StandAloneSig),
                        Handle(2, TableIndex.AssemblyRef));

                    var method2 = compilation2.GetMember<MethodSymbol>("C.F2");
                    var diff2 = compilation2.EmitDifference(
                        diff1.NextGeneration,
                        ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method1, method2)));

                    // Verify delta metadata contains expected rows.
                    using (var md2 = diff2.GetMetadata())
                    {
                        var reader2 = md2.Reader;
                        readers = new[] { reader0, reader1, reader2 };
                        EncValidation.VerifyModuleMvid(2, reader1, reader2);
                        CheckNames(readers, reader2.GetTypeDefNames());
                        CheckNames(readers, reader2.GetMethodDefNames(), "F2");
                        CheckNames(readers, reader2.GetParameterDefNames());
                        CheckEncLog(reader2,
                            Row(3, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                            Row(8, TableIndex.TypeRef, EditAndContinueOperation.Default),
                            Row(3, TableIndex.StandAloneSig, EditAndContinueOperation.Default),
                            Row(5, TableIndex.MethodDef, EditAndContinueOperation.Default)); // C.F2
                        CheckEncMap(reader2,
                            Handle(8, TableIndex.TypeRef),
                            Handle(5, TableIndex.MethodDef),
                            Handle(3, TableIndex.StandAloneSig),
                            Handle(3, TableIndex.AssemblyRef));
                    }
                }
            }
        }

        [Fact]
        public void AddField()
        {
            var source0 =
@"class C
{
    string F = ""F"";
}";
            var source1 =
@"class C
{
    string F = ""F"";
    string G = ""G"";
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);

            // Verify full metadata contains expected rows.
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "C");
                CheckNames(reader0, reader0.GetFieldDefNames(), "F");
                CheckNames(reader0, reader0.GetMethodDefNames(), ".ctor");

                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);
                var method0 = compilation0.GetMember<MethodSymbol>("C..ctor");
                var method1 = compilation1.GetMember<MethodSymbol>("C..ctor");

                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(
                        new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<FieldSymbol>("C.G")),
                        new SemanticEdit(SemanticEditKind.Update, method0, method1)));

                // Verify delta metadata contains expected rows.
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    CheckNames(readers, reader1.GetTypeDefNames());
                    CheckNames(readers, reader1.GetFieldDefNames(), "G");
                    CheckNames(readers, reader1.GetMethodDefNames(), ".ctor");
                    CheckEncLog(reader1,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(5, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(6, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.TypeDef, EditAndContinueOperation.AddField),
                        Row(2, TableIndex.Field, EditAndContinueOperation.Default),
                        Row(1, TableIndex.MethodDef, EditAndContinueOperation.Default));
                    CheckEncMap(reader1,
                        Handle(6, TableIndex.TypeRef),
                        Handle(2, TableIndex.Field),
                        Handle(1, TableIndex.MethodDef),
                        Handle(5, TableIndex.MemberRef),
                        Handle(2, TableIndex.AssemblyRef));
                }
            }
        }

        [Fact]
        public void ModifyProperty()
        {
            var source0 =
@"class C
{
    object P { get { return 1; } }
}";
            var source1 =
@"class C
{
    object P { get { return 2; } }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);

            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetPropertyDefNames(), "P");
                CheckNames(reader0, reader0.GetMethodDefNames(), "get_P", ".ctor");
                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);

                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation0.GetMember<MethodSymbol>("C.get_P"), compilation1.GetMember<MethodSymbol>("C.get_P"))));
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    CheckNames(readers, reader1.GetPropertyDefNames(), "P");
                    CheckNames(readers, reader1.GetMethodDefNames(), "get_P");
                    CheckEncLog(reader1,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(7, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(8, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.StandAloneSig, EditAndContinueOperation.Default),
                        Row(1, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(1, TableIndex.Property, EditAndContinueOperation.Default),
                        Row(2, TableIndex.MethodSemantics, EditAndContinueOperation.Default));
                    CheckEncMap(reader1,
                        Handle(7, TableIndex.TypeRef),
                        Handle(8, TableIndex.TypeRef),
                        Handle(1, TableIndex.MethodDef),
                        Handle(2, TableIndex.StandAloneSig),
                        Handle(1, TableIndex.Property),
                        Handle(2, TableIndex.MethodSemantics),
                        Handle(2, TableIndex.AssemblyRef));
                }
            }
        }

        [Fact]
        public void AddProperty()
        {
            var source0 =
@"class A
{
    object P { get; set; }
}
class B
{
}";
            var source1 =
@"class A
{
    object P { get; set; }
}
class B
{
    object R { get { return null; } }
}";
            var source2 =
@"class A
{
    object P { get; set; }
    object Q { get; set; }
}
class B
{
    object R { get { return null; } }
    object S { set { } }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation0.WithSource(source2);

            // Verify full metadata contains expected rows.
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "A", "B");
                CheckNames(reader0, reader0.GetFieldDefNames(), "<P>k__BackingField");
                CheckNames(reader0, reader0.GetPropertyDefNames(), "P");
                CheckNames(reader0, reader0.GetMethodDefNames(), "get_P", "set_P", ".ctor", ".ctor");

                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);

                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<PropertySymbol>("B.R"))));

                // Verify delta metadata contains expected rows.
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    CheckNames(readers, reader1.GetTypeDefNames());
                    CheckNames(readers, reader1.GetFieldDefNames());
                    CheckNames(readers, reader1.GetPropertyDefNames(), "R");
                    CheckNames(readers, reader1.GetMethodDefNames(), "get_R");
                    CheckEncLog(reader1,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(9, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.StandAloneSig, EditAndContinueOperation.Default),
                        Row(2, TableIndex.PropertyMap, EditAndContinueOperation.Default),
                        Row(3, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(5, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.PropertyMap, EditAndContinueOperation.AddProperty),
                        Row(2, TableIndex.Property, EditAndContinueOperation.Default),
                        Row(3, TableIndex.MethodSemantics, EditAndContinueOperation.Default));
                    CheckEncMap(reader1,
                        Handle(9, TableIndex.TypeRef),
                        Handle(5, TableIndex.MethodDef),
                        Handle(2, TableIndex.StandAloneSig),
                        Handle(2, TableIndex.PropertyMap),
                        Handle(2, TableIndex.Property),
                        Handle(3, TableIndex.MethodSemantics),
                        Handle(2, TableIndex.AssemblyRef));

                    var diff2 = compilation2.EmitDifference(
                        diff1.NextGeneration,
                        ImmutableArray.Create(
                            new SemanticEdit(SemanticEditKind.Insert, null, compilation2.GetMember<PropertySymbol>("A.Q")),
                            new SemanticEdit(SemanticEditKind.Insert, null, compilation2.GetMember<PropertySymbol>("B.S"))));

                    // Verify delta metadata contains expected rows.
                    using (var md2 = diff2.GetMetadata())
                    {
                        var reader2 = md2.Reader;
                        readers = new[] { reader0, reader1, reader2 };
                        CheckNames(readers, reader2.GetTypeDefNames());
                        CheckNames(readers, reader2.GetFieldDefNames(), "<Q>k__BackingField");
                        CheckNames(readers, reader2.GetPropertyDefNames(), "Q", "S");
                        CheckNames(readers, reader2.GetMethodDefNames(), "get_Q", "set_Q", "set_S");
                        CheckEncLog(reader2,
                            Row(3, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                            Row(7, TableIndex.MemberRef, EditAndContinueOperation.Default),
                            Row(8, TableIndex.MemberRef, EditAndContinueOperation.Default),
                            Row(10, TableIndex.TypeRef, EditAndContinueOperation.Default),
                            Row(11, TableIndex.TypeRef, EditAndContinueOperation.Default),
                            Row(12, TableIndex.TypeRef, EditAndContinueOperation.Default),
                            Row(13, TableIndex.TypeRef, EditAndContinueOperation.Default),
                            Row(3, TableIndex.StandAloneSig, EditAndContinueOperation.Default),
                            Row(2, TableIndex.TypeDef, EditAndContinueOperation.AddField),
                            Row(2, TableIndex.Field, EditAndContinueOperation.Default),
                            Row(2, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                            Row(6, TableIndex.MethodDef, EditAndContinueOperation.Default),
                            Row(2, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                            Row(7, TableIndex.MethodDef, EditAndContinueOperation.Default),
                            Row(3, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                            Row(8, TableIndex.MethodDef, EditAndContinueOperation.Default),
                            Row(1, TableIndex.PropertyMap, EditAndContinueOperation.AddProperty),
                            Row(3, TableIndex.Property, EditAndContinueOperation.Default),
                            Row(2, TableIndex.PropertyMap, EditAndContinueOperation.AddProperty),
                            Row(4, TableIndex.Property, EditAndContinueOperation.Default),
                            Row(7, TableIndex.MethodDef, EditAndContinueOperation.AddParameter),
                            Row(2, TableIndex.Param, EditAndContinueOperation.Default),
                            Row(8, TableIndex.MethodDef, EditAndContinueOperation.AddParameter),
                            Row(3, TableIndex.Param, EditAndContinueOperation.Default),
                            Row(8, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                            Row(9, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                            Row(10, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                            Row(11, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                            Row(4, TableIndex.MethodSemantics, EditAndContinueOperation.Default),
                            Row(5, TableIndex.MethodSemantics, EditAndContinueOperation.Default),
                            Row(6, TableIndex.MethodSemantics, EditAndContinueOperation.Default));
                        CheckEncMap(reader2,
                            Handle(10, TableIndex.TypeRef),
                            Handle(11, TableIndex.TypeRef),
                            Handle(12, TableIndex.TypeRef),
                            Handle(13, TableIndex.TypeRef),
                            Handle(2, TableIndex.Field),
                            Handle(6, TableIndex.MethodDef),
                            Handle(7, TableIndex.MethodDef),
                            Handle(8, TableIndex.MethodDef),
                            Handle(2, TableIndex.Param),
                            Handle(3, TableIndex.Param),
                            Handle(7, TableIndex.MemberRef),
                            Handle(8, TableIndex.MemberRef),
                            Handle(8, TableIndex.CustomAttribute),
                            Handle(9, TableIndex.CustomAttribute),
                            Handle(10, TableIndex.CustomAttribute),
                            Handle(11, TableIndex.CustomAttribute),
                            Handle(3, TableIndex.StandAloneSig),
                            Handle(3, TableIndex.Property),
                            Handle(4, TableIndex.Property),
                            Handle(4, TableIndex.MethodSemantics),
                            Handle(5, TableIndex.MethodSemantics),
                            Handle(6, TableIndex.MethodSemantics),
                            Handle(3, TableIndex.AssemblyRef));
                    }
                }
            }
        }

        [Fact]
        public void AddEvent()
        {
            var source0 =
@"delegate void D();
class A
{
    event D E;
}
class B
{
}";
            var source1 =
@"delegate void D();
class A
{
    event D E;
}
class B
{
    event D F;
}";
            var source2 =
@"delegate void D();
class A
{
    event D E;
    event D G;
}
class B
{
    event D F;
    event D H;
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation0.WithSource(source2);

            // Verify full metadata contains expected rows.
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "D", "A", "B");
                CheckNames(reader0, reader0.GetFieldDefNames(), "E");
                CheckNames(reader0, reader0.GetEventDefNames(), "E");
                CheckNames(reader0, reader0.GetMethodDefNames(), ".ctor", "Invoke", "BeginInvoke", "EndInvoke", "add_E", "remove_E", ".ctor", ".ctor");

                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);

                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<EventSymbol>("B.F"))));

                // Verify delta metadata contains expected rows.
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    CheckNames(readers, reader1.GetTypeDefNames());
                    CheckNames(readers, reader1.GetFieldDefNames(), "F");
                    CheckNames(readers, reader1.GetMethodDefNames(), "add_F", "remove_F");
                    CheckEncLog(reader1,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(10, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(11, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(12, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(13, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(14, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.MethodSpec, EditAndContinueOperation.Default),
                        Row(14, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(15, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(16, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(17, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(18, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(19, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.StandAloneSig, EditAndContinueOperation.Default),
                        Row(2, TableIndex.EventMap, EditAndContinueOperation.Default),
                        Row(2, TableIndex.EventMap, EditAndContinueOperation.AddEvent),
                        Row(2, TableIndex.Event, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddField),
                        Row(2, TableIndex.Field, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(9, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(10, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(9, TableIndex.MethodDef, EditAndContinueOperation.AddParameter),
                        Row(8, TableIndex.Param, EditAndContinueOperation.Default),
                        Row(10, TableIndex.MethodDef, EditAndContinueOperation.AddParameter),
                        Row(9, TableIndex.Param, EditAndContinueOperation.Default),
                        Row(8, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                        Row(9, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                        Row(10, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                        Row(11, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                        Row(3, TableIndex.MethodSemantics, EditAndContinueOperation.Default),
                        Row(4, TableIndex.MethodSemantics, EditAndContinueOperation.Default));
                    CheckEncMap(reader1,
                        Handle(14, TableIndex.TypeRef),
                        Handle(15, TableIndex.TypeRef),
                        Handle(16, TableIndex.TypeRef),
                        Handle(17, TableIndex.TypeRef),
                        Handle(18, TableIndex.TypeRef),
                        Handle(19, TableIndex.TypeRef),
                        Handle(2, TableIndex.Field),
                        Handle(9, TableIndex.MethodDef),
                        Handle(10, TableIndex.MethodDef),
                        Handle(8, TableIndex.Param),
                        Handle(9, TableIndex.Param),
                        Handle(10, TableIndex.MemberRef),
                        Handle(11, TableIndex.MemberRef),
                        Handle(12, TableIndex.MemberRef),
                        Handle(13, TableIndex.MemberRef),
                        Handle(14, TableIndex.MemberRef),
                        Handle(8, TableIndex.CustomAttribute),
                        Handle(9, TableIndex.CustomAttribute),
                        Handle(10, TableIndex.CustomAttribute),
                        Handle(11, TableIndex.CustomAttribute),
                        Handle(2, TableIndex.StandAloneSig),
                        Handle(2, TableIndex.EventMap),
                        Handle(2, TableIndex.Event),
                        Handle(3, TableIndex.MethodSemantics),
                        Handle(4, TableIndex.MethodSemantics),
                        Handle(2, TableIndex.AssemblyRef),
                        Handle(2, TableIndex.MethodSpec));

                    var diff2 = compilation2.EmitDifference(
                        diff1.NextGeneration,
                        ImmutableArray.Create(
                            new SemanticEdit(SemanticEditKind.Insert, null, compilation2.GetMember<EventSymbol>("A.G")),
                            new SemanticEdit(SemanticEditKind.Insert, null, compilation2.GetMember<EventSymbol>("B.H"))));

                    // Verify delta metadata contains expected rows.
                    using (var md2 = diff2.GetMetadata())
                    {
                        var reader2 = md2.Reader;
                        readers = new[] { reader0, reader1, reader2 };
                        CheckNames(readers, reader2.GetTypeDefNames());
                        CheckNames(readers, reader2.GetFieldDefNames(), "G", "H");
                        CheckNames(readers, reader2.GetMethodDefNames(), "add_G", "remove_G", "add_H", "remove_H");
                        CheckEncLog(reader2,
                            Row(3, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                            Row(15, TableIndex.MemberRef, EditAndContinueOperation.Default),
                            Row(16, TableIndex.MemberRef, EditAndContinueOperation.Default),
                            Row(17, TableIndex.MemberRef, EditAndContinueOperation.Default),
                            Row(18, TableIndex.MemberRef, EditAndContinueOperation.Default),
                            Row(19, TableIndex.MemberRef, EditAndContinueOperation.Default),
                            Row(3, TableIndex.MethodSpec, EditAndContinueOperation.Default),
                            Row(20, TableIndex.TypeRef, EditAndContinueOperation.Default),
                            Row(21, TableIndex.TypeRef, EditAndContinueOperation.Default),
                            Row(22, TableIndex.TypeRef, EditAndContinueOperation.Default),
                            Row(23, TableIndex.TypeRef, EditAndContinueOperation.Default),
                            Row(24, TableIndex.TypeRef, EditAndContinueOperation.Default),
                            Row(25, TableIndex.TypeRef, EditAndContinueOperation.Default),
                            Row(3, TableIndex.StandAloneSig, EditAndContinueOperation.Default),
                            Row(1, TableIndex.EventMap, EditAndContinueOperation.AddEvent),
                            Row(3, TableIndex.Event, EditAndContinueOperation.Default),
                            Row(2, TableIndex.EventMap, EditAndContinueOperation.AddEvent),
                            Row(4, TableIndex.Event, EditAndContinueOperation.Default),
                            Row(3, TableIndex.TypeDef, EditAndContinueOperation.AddField),
                            Row(3, TableIndex.Field, EditAndContinueOperation.Default),
                            Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddField),
                            Row(4, TableIndex.Field, EditAndContinueOperation.Default),
                            Row(3, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                            Row(11, TableIndex.MethodDef, EditAndContinueOperation.Default),
                            Row(3, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                            Row(12, TableIndex.MethodDef, EditAndContinueOperation.Default),
                            Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                            Row(13, TableIndex.MethodDef, EditAndContinueOperation.Default),
                            Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                            Row(14, TableIndex.MethodDef, EditAndContinueOperation.Default),
                            Row(11, TableIndex.MethodDef, EditAndContinueOperation.AddParameter),
                            Row(10, TableIndex.Param, EditAndContinueOperation.Default),
                            Row(12, TableIndex.MethodDef, EditAndContinueOperation.AddParameter),
                            Row(11, TableIndex.Param, EditAndContinueOperation.Default),
                            Row(13, TableIndex.MethodDef, EditAndContinueOperation.AddParameter),
                            Row(12, TableIndex.Param, EditAndContinueOperation.Default),
                            Row(14, TableIndex.MethodDef, EditAndContinueOperation.AddParameter),
                            Row(13, TableIndex.Param, EditAndContinueOperation.Default),
                            Row(12, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                            Row(13, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                            Row(14, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                            Row(15, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                            Row(16, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                            Row(17, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                            Row(18, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                            Row(19, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                            Row(5, TableIndex.MethodSemantics, EditAndContinueOperation.Default),
                            Row(6, TableIndex.MethodSemantics, EditAndContinueOperation.Default),
                            Row(7, TableIndex.MethodSemantics, EditAndContinueOperation.Default),
                            Row(8, TableIndex.MethodSemantics, EditAndContinueOperation.Default));
                        CheckEncMap(reader2,
                            Handle(20, TableIndex.TypeRef),
                            Handle(21, TableIndex.TypeRef),
                            Handle(22, TableIndex.TypeRef),
                            Handle(23, TableIndex.TypeRef),
                            Handle(24, TableIndex.TypeRef),
                            Handle(25, TableIndex.TypeRef),
                            Handle(3, TableIndex.Field),
                            Handle(4, TableIndex.Field),
                            Handle(11, TableIndex.MethodDef),
                            Handle(12, TableIndex.MethodDef),
                            Handle(13, TableIndex.MethodDef),
                            Handle(14, TableIndex.MethodDef),
                            Handle(10, TableIndex.Param),
                            Handle(11, TableIndex.Param),
                            Handle(12, TableIndex.Param),
                            Handle(13, TableIndex.Param),
                            Handle(15, TableIndex.MemberRef),
                            Handle(16, TableIndex.MemberRef),
                            Handle(17, TableIndex.MemberRef),
                            Handle(18, TableIndex.MemberRef),
                            Handle(19, TableIndex.MemberRef),
                            Handle(12, TableIndex.CustomAttribute),
                            Handle(13, TableIndex.CustomAttribute),
                            Handle(14, TableIndex.CustomAttribute),
                            Handle(15, TableIndex.CustomAttribute),
                            Handle(16, TableIndex.CustomAttribute),
                            Handle(17, TableIndex.CustomAttribute),
                            Handle(18, TableIndex.CustomAttribute),
                            Handle(19, TableIndex.CustomAttribute),
                            Handle(3, TableIndex.StandAloneSig),
                            Handle(3, TableIndex.Event),
                            Handle(4, TableIndex.Event),
                            Handle(5, TableIndex.MethodSemantics),
                            Handle(6, TableIndex.MethodSemantics),
                            Handle(7, TableIndex.MethodSemantics),
                            Handle(8, TableIndex.MethodSemantics),
                            Handle(3, TableIndex.AssemblyRef),
                            Handle(3, TableIndex.MethodSpec));
                    }
                }
            }
        }

        [Fact]
        public void AddNestedTypeAndMembers()
        {
            var source0 =
@"class A
{
    class B { }
    static object F()
    {
        return new B();
    }
}";
            var source1 =
@"class A
{
    class B { }
    class C
    {
        class D { }
        static object F;
        internal static object G()
        {
            return F;
        }
    }
    static object F()
    {
        return C.G();
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);

            // Verify full metadata contains expected rows.
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "A", "B");
                CheckNames(reader0, reader0.GetMethodDefNames(), "F", ".ctor", ".ctor");
                Assert.Equal(1, reader0.GetTableRowCount(TableIndex.NestedClass));

                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);

                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(
                        new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<NamedTypeSymbol>("A.C")),
                        new SemanticEdit(SemanticEditKind.Update, compilation0.GetMember<MethodSymbol>("A.F"), compilation1.GetMember<MethodSymbol>("A.F"))));

                // Verify delta metadata contains expected rows.
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    CheckNames(readers, reader1.GetTypeDefNames(), "C", "D");
                    Assert.Equal(2, reader1.GetTableRowCount(TableIndex.NestedClass));
                    CheckNames(readers, reader1.GetMethodDefNames(), "F", "G", ".ctor", ".ctor");
                    CheckEncLog(reader1,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(5, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(6, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.StandAloneSig, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.Default),
                        Row(5, TableIndex.TypeDef, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddField),
                        Row(1, TableIndex.Field, EditAndContinueOperation.Default),
                        Row(1, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(4, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(5, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(5, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(6, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.NestedClass, EditAndContinueOperation.Default),
                        Row(3, TableIndex.NestedClass, EditAndContinueOperation.Default));
                    CheckEncMap(reader1,
                        Handle(6, TableIndex.TypeRef),
                        Handle(4, TableIndex.TypeDef),
                        Handle(5, TableIndex.TypeDef),
                        Handle(1, TableIndex.Field),
                        Handle(1, TableIndex.MethodDef),
                        Handle(4, TableIndex.MethodDef),
                        Handle(5, TableIndex.MethodDef),
                        Handle(6, TableIndex.MethodDef),
                        Handle(5, TableIndex.MemberRef),
                        Handle(2, TableIndex.StandAloneSig),
                        Handle(2, TableIndex.AssemblyRef),
                        Handle(2, TableIndex.NestedClass),
                        Handle(3, TableIndex.NestedClass));
                }
            }
        }

        /// <summary>
        /// Nested types should be emitted in the
        /// same order as full emit.
        /// </summary>
        [Fact]
        public void AddNestedTypesOrder()
        {
            var source0 =
@"class A
{
    class B1
    {
        class C1 { }
    }
    class B2
    {
        class C2 { }
    }
}";
            var source1 =
@"class A
{
    class B1
    {
        class C1 { }
    }
    class B2
    {
        class C2 { }
    }
    class B3
    {
        class C3 { }
    }
    class B4
    {
        class C4 { }
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);

            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "A", "B1", "B2", "C1", "C2");
                Assert.Equal(4, reader0.GetTableRowCount(TableIndex.NestedClass));

                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(
                        new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<NamedTypeSymbol>("A.B3")),
                        new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<NamedTypeSymbol>("A.B4"))));

                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    CheckNames(readers, reader1.GetTypeDefNames(), "B3", "B4", "C3", "C4");
                    Assert.Equal(4, reader1.GetTableRowCount(TableIndex.NestedClass));
                }
            }
        }

        [Fact]
        public void AddNestedGenericType()
        {
            var source0 =
@"class A
{
    class B<T>
    {
    }
    static object F()
    {
        return null;
    }
}";
            var source1 =
@"class A
{
    class B<T>
    {
        internal class C<U>
        {
            internal object F<V>() where V : T, new()
            {
                return new C<V>();
            }
        }
    }
    static object F()
    {
        return new B<A>.C<B<object>>().F<A>();
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);

            // Verify full metadata contains expected rows.
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "A", "B`1");
                Assert.Equal(1, reader0.GetTableRowCount(TableIndex.NestedClass));

                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);

                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(
                        new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<NamedTypeSymbol>("A.B.C")),
                        new SemanticEdit(SemanticEditKind.Update, compilation0.GetMember<MethodSymbol>("A.F"), compilation1.GetMember<MethodSymbol>("A.F"))));

                // Verify delta metadata contains expected rows.
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    CheckNames(readers, reader1.GetTypeDefNames(), "C`1");
                    Assert.Equal(1, reader1.GetTableRowCount(TableIndex.NestedClass));
                    CheckEncLog(reader1,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(5, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(6, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(7, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(8, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(1, TableIndex.MethodSpec, EditAndContinueOperation.Default),
                        Row(6, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(1, TableIndex.TypeSpec, EditAndContinueOperation.Default),
                        Row(2, TableIndex.TypeSpec, EditAndContinueOperation.Default),
                        Row(3, TableIndex.TypeSpec, EditAndContinueOperation.Default),
                        Row(2, TableIndex.StandAloneSig, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.Default),
                        Row(1, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(4, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(5, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.NestedClass, EditAndContinueOperation.Default),
                        Row(2, TableIndex.GenericParam, EditAndContinueOperation.Default),
                        Row(3, TableIndex.GenericParam, EditAndContinueOperation.Default),
                        Row(4, TableIndex.GenericParam, EditAndContinueOperation.Default),
                        Row(1, TableIndex.GenericParamConstraint, EditAndContinueOperation.Default));
                    CheckEncMap(reader1,
                        Handle(6, TableIndex.TypeRef),
                        Handle(4, TableIndex.TypeDef),
                        Handle(1, TableIndex.MethodDef),
                        Handle(4, TableIndex.MethodDef),
                        Handle(5, TableIndex.MethodDef),
                        Handle(5, TableIndex.MemberRef),
                        Handle(6, TableIndex.MemberRef),
                        Handle(7, TableIndex.MemberRef),
                        Handle(8, TableIndex.MemberRef),
                        Handle(2, TableIndex.StandAloneSig),
                        Handle(1, TableIndex.TypeSpec),
                        Handle(2, TableIndex.TypeSpec),
                        Handle(3, TableIndex.TypeSpec),
                        Handle(2, TableIndex.AssemblyRef),
                        Handle(2, TableIndex.NestedClass),
                        Handle(2, TableIndex.GenericParam),
                        Handle(3, TableIndex.GenericParam),
                        Handle(4, TableIndex.GenericParam),
                        Handle(1, TableIndex.MethodSpec),
                        Handle(1, TableIndex.GenericParamConstraint));
                }
            }
        }

        [Fact]
        public void ModifyExplicitImplementation()
        {
            var source =
@"interface I
{
    void M();
}
class C : I
{
    void I.M() { }
}";
            var compilation0 = CreateCompilationWithMscorlib(source, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source);

            // Verify full metadata contains expected rows.
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "I", "C");
                CheckNames(reader0, reader0.GetMethodDefNames(), "M", "I.M", ".ctor");

                var method0 = compilation0.GetMember<NamedTypeSymbol>("C").GetMember<MethodSymbol>("I.M");
                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);
                var method1 = compilation1.GetMember<NamedTypeSymbol>("C").GetMember<MethodSymbol>("I.M");
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1)));

                // Verify delta metadata contains expected rows.
                using (var block1 = diff1.GetMetadata())
                {
                    var reader1 = block1.Reader;
                    var readers = new[] { reader0, reader1 };
                    EncValidation.VerifyModuleMvid(1, reader0, reader1);
                    CheckNames(readers, reader1.GetTypeDefNames());
                    CheckNames(readers, reader1.GetMethodDefNames(), "I.M");
                    CheckEncLog(reader1,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(6, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.MethodDef, EditAndContinueOperation.Default));
                    CheckEncMap(reader1,
                        Handle(6, TableIndex.TypeRef),
                        Handle(2, TableIndex.MethodDef),
                        Handle(2, TableIndex.AssemblyRef));
                }
            }
        }

        [Fact]
        public void AddThenModifyExplicitImplementation()
        {
            var source0 =
@"interface I
{
    void M();
}
class A : I
{
    void I.M() { }
}
class B : I
{
    public void M() { }
}";
            var source1 =
@"interface I
{
    void M();
}
class A : I
{
    void I.M() { }
}
class B : I
{
    public void M() { }
    void I.M() { }
}";
            var source2 = source1;
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation0.WithSource(source2);

            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);
                var method1 = compilation1.GetMember<NamedTypeSymbol>("B").GetMember<MethodSymbol>("I.M");
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Insert, null, method1)));

                using (var block1 = diff1.GetMetadata())
                {
                    var reader1 = block1.Reader;
                    var readers = new[] { reader0, reader1 };
                    EncValidation.VerifyModuleMvid(1, reader0, reader1);
                    CheckNames(readers, reader1.GetMethodDefNames(), "I.M");
                    CheckEncLog(reader1,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(6, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(6, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.MethodImpl, EditAndContinueOperation.Default));
                    CheckEncMap(reader1,
                        Handle(6, TableIndex.TypeRef),
                        Handle(6, TableIndex.MethodDef),
                        Handle(2, TableIndex.MethodImpl),
                        Handle(2, TableIndex.AssemblyRef));

                    var generation1 = diff1.NextGeneration;
                    var method2 = compilation2.GetMember<NamedTypeSymbol>("B").GetMember<MethodSymbol>("I.M");
                    var diff2 = compilation2.EmitDifference(
                        generation1,
                        ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method1, method2)));

                    using (var md2 = diff2.GetMetadata())
                    {
                        var reader2 = md2.Reader;
                        readers = new[] { reader0, reader1, reader2 };
                        EncValidation.VerifyModuleMvid(2, reader1, reader2);
                        CheckNames(readers, reader2.GetMethodDefNames(), "I.M");
                        CheckEncLog(reader2,
                            Row(3, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                            Row(7, TableIndex.TypeRef, EditAndContinueOperation.Default),
                            Row(6, TableIndex.MethodDef, EditAndContinueOperation.Default));
                        CheckEncMap(reader2,
                            Handle(7, TableIndex.TypeRef),
                            Handle(6, TableIndex.MethodDef),
                            Handle(3, TableIndex.AssemblyRef));
                    }
                }
            }
        }

        [Fact, WorkItem(930065)]
        public void ModifyConstructorBodyInPresenceOfExplicitInterfaceImplementation()
        {
            var source = @"
interface I
{
    void M();
}
class C : I
{
    public C()
    {
    }
    void I.M() { }
}
";
            var compilation0 = CreateCompilationWithMscorlib(source, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source);

            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;

                var method0 = compilation0.GetMember<NamedTypeSymbol>("C").InstanceConstructors.Single();
                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);
                var method1 = compilation1.GetMember<NamedTypeSymbol>("C").InstanceConstructors.Single();
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1)));

                using (var block1 = diff1.GetMetadata())
                {
                    var reader1 = block1.Reader;
                    var readers = new[] { reader0, reader1 };
                    EncValidation.VerifyModuleMvid(1, reader0, reader1);
                    CheckNames(readers, reader1.GetTypeDefNames());
                    CheckNames(readers, reader1.GetMethodDefNames(), ".ctor");
                    CheckEncLog(reader1,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(5, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(6, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.MethodDef, EditAndContinueOperation.Default));
                    CheckEncMap(reader1,
                        Handle(6, TableIndex.TypeRef),
                        Handle(2, TableIndex.MethodDef),
                        Handle(5, TableIndex.MemberRef),
                        Handle(2, TableIndex.AssemblyRef));
                }
            }
        }

        [Fact]
        public void AddAttributeReferences()
        {
            var source0 =
@"class A : System.Attribute { }
class B : System.Attribute { }
class C
{
    [A] static void M1<[B]T>() { }
    [B] static object F1;
    [A] static object P1 { get { return null; } }
    [B] static event D E1;
}
delegate void D();
";
            var source1 =
@"class A : System.Attribute { }
class B : System.Attribute { }
class C
{
    [A] static void M1<[B]T>() { }
    [B] static void M2<[A]T>() { }
    [B] static object F1;
    [A] static object F2;
    [A] static object P1 { get { return null; } }
    [B] static object P2 { get { return null; } }
    [B] static event D E1;
    [A] static event D E2;
}
delegate void D();
";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);

            // Verify full metadata contains expected rows.
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "A", "B", "C", "D");
                CheckNames(reader0, reader0.GetMethodDefNames(), ".ctor", ".ctor", "M1", "get_P1", "add_E1", "remove_E1", ".ctor", ".ctor", "Invoke", "BeginInvoke", "EndInvoke");
                CheckAttributes(reader0,
                    new CustomAttributeRow(Handle(1, TableIndex.Field), Handle(2, TableIndex.MethodDef)),
                    new CustomAttributeRow(Handle(1, TableIndex.Property), Handle(1, TableIndex.MethodDef)),
                    new CustomAttributeRow(Handle(1, TableIndex.Event), Handle(2, TableIndex.MethodDef)),
                    new CustomAttributeRow(Handle(1, TableIndex.Assembly), Handle(1, TableIndex.MemberRef)),
                    new CustomAttributeRow(Handle(1, TableIndex.Assembly), Handle(2, TableIndex.MemberRef)),
                    new CustomAttributeRow(Handle(1, TableIndex.Assembly), Handle(3, TableIndex.MemberRef)),
                    new CustomAttributeRow(Handle(1, TableIndex.GenericParam), Handle(2, TableIndex.MethodDef)),
                    new CustomAttributeRow(Handle(2, TableIndex.Field), Handle(4, TableIndex.MemberRef)),
                    new CustomAttributeRow(Handle(2, TableIndex.Field), Handle(5, TableIndex.MemberRef)),
                    new CustomAttributeRow(Handle(3, TableIndex.MethodDef), Handle(1, TableIndex.MethodDef)),
                    new CustomAttributeRow(Handle(5, TableIndex.MethodDef), Handle(4, TableIndex.MemberRef)),
                    new CustomAttributeRow(Handle(6, TableIndex.MethodDef), Handle(4, TableIndex.MemberRef)));

                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);

                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(
                        new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<MethodSymbol>("C.M2")),
                        new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<FieldSymbol>("C.F2")),
                        new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<PropertySymbol>("C.P2")),
                        new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<EventSymbol>("C.E2"))));

                // Verify delta metadata contains expected rows.
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    CheckNames(readers, reader1.GetTypeDefNames());
                    CheckNames(readers, reader1.GetMethodDefNames(), "M2", "get_P2", "add_E2", "remove_E2");
                    CheckEncLog(reader1,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(11, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(12, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(13, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(14, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(15, TableIndex.MemberRef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.MethodSpec, EditAndContinueOperation.Default),
                        Row(15, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(16, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(17, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(18, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(19, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(20, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(3, TableIndex.StandAloneSig, EditAndContinueOperation.Default),
                        Row(4, TableIndex.StandAloneSig, EditAndContinueOperation.Default),
                        Row(1, TableIndex.EventMap, EditAndContinueOperation.AddEvent),
                        Row(2, TableIndex.Event, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddField),
                        Row(3, TableIndex.Field, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddField),
                        Row(4, TableIndex.Field, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(12, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(13, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(14, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(4, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(15, TableIndex.MethodDef, EditAndContinueOperation.Default),
                        Row(1, TableIndex.PropertyMap, EditAndContinueOperation.AddProperty),
                        Row(2, TableIndex.Property, EditAndContinueOperation.Default),
                        Row(14, TableIndex.MethodDef, EditAndContinueOperation.AddParameter),
                        Row(8, TableIndex.Param, EditAndContinueOperation.Default),
                        Row(15, TableIndex.MethodDef, EditAndContinueOperation.AddParameter),
                        Row(9, TableIndex.Param, EditAndContinueOperation.Default),
                        Row(13, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                        Row(14, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                        Row(15, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                        Row(16, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                        Row(17, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                        Row(18, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                        Row(19, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                        Row(20, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                        Row(21, TableIndex.CustomAttribute, EditAndContinueOperation.Default),
                        Row(4, TableIndex.MethodSemantics, EditAndContinueOperation.Default),
                        Row(5, TableIndex.MethodSemantics, EditAndContinueOperation.Default),
                        Row(6, TableIndex.MethodSemantics, EditAndContinueOperation.Default),
                        Row(2, TableIndex.GenericParam, EditAndContinueOperation.Default));
                    CheckEncMap(reader1,
                        Handle(15, TableIndex.TypeRef),
                        Handle(16, TableIndex.TypeRef),
                        Handle(17, TableIndex.TypeRef),
                        Handle(18, TableIndex.TypeRef),
                        Handle(19, TableIndex.TypeRef),
                        Handle(20, TableIndex.TypeRef),
                        Handle(3, TableIndex.Field),
                        Handle(4, TableIndex.Field),
                        Handle(12, TableIndex.MethodDef),
                        Handle(13, TableIndex.MethodDef),
                        Handle(14, TableIndex.MethodDef),
                        Handle(15, TableIndex.MethodDef),
                        Handle(8, TableIndex.Param),
                        Handle(9, TableIndex.Param),
                        Handle(11, TableIndex.MemberRef),
                        Handle(12, TableIndex.MemberRef),
                        Handle(13, TableIndex.MemberRef),
                        Handle(14, TableIndex.MemberRef),
                        Handle(15, TableIndex.MemberRef),
                        Handle(13, TableIndex.CustomAttribute),
                        Handle(14, TableIndex.CustomAttribute),
                        Handle(15, TableIndex.CustomAttribute),
                        Handle(16, TableIndex.CustomAttribute),
                        Handle(17, TableIndex.CustomAttribute),
                        Handle(18, TableIndex.CustomAttribute),
                        Handle(19, TableIndex.CustomAttribute),
                        Handle(20, TableIndex.CustomAttribute),
                        Handle(21, TableIndex.CustomAttribute),
                        Handle(3, TableIndex.StandAloneSig),
                        Handle(4, TableIndex.StandAloneSig),
                        Handle(2, TableIndex.Event),
                        Handle(2, TableIndex.Property),
                        Handle(4, TableIndex.MethodSemantics),
                        Handle(5, TableIndex.MethodSemantics),
                        Handle(6, TableIndex.MethodSemantics),
                        Handle(2, TableIndex.AssemblyRef),
                        Handle(2, TableIndex.GenericParam),
                        Handle(2, TableIndex.MethodSpec));
                    CheckAttributes(reader1,
                        new CustomAttributeRow(Handle(1, TableIndex.GenericParam), Handle(1, TableIndex.MethodDef)),
                        new CustomAttributeRow(Handle(2, TableIndex.Property), Handle(2, TableIndex.MethodDef)),
                        new CustomAttributeRow(Handle(2, TableIndex.Event), Handle(1, TableIndex.MethodDef)),
                        new CustomAttributeRow(Handle(3, TableIndex.Field), Handle(1, TableIndex.MethodDef)),
                        new CustomAttributeRow(Handle(4, TableIndex.Field), Handle(11, TableIndex.MemberRef)),
                        new CustomAttributeRow(Handle(4, TableIndex.Field), Handle(12, TableIndex.MemberRef)),
                        new CustomAttributeRow(Handle(12, TableIndex.MethodDef), Handle(2, TableIndex.MethodDef)),
                        new CustomAttributeRow(Handle(14, TableIndex.MethodDef), Handle(11, TableIndex.MemberRef)),
                        new CustomAttributeRow(Handle(15, TableIndex.MethodDef), Handle(11, TableIndex.MemberRef)));
                }
            }
        }

        /// <summary>
        /// [assembly: ...] and [module: ...] attributes should
        /// not be included in delta metadata.
        /// </summary>
        [Fact]
        public void AssemblyAndModuleAttributeReferences()
        {
            var source0 =
@"[assembly: System.CLSCompliantAttribute(true)]
[module: System.CLSCompliantAttribute(true)]
class C
{
}";
            var source1 =
@"[assembly: System.CLSCompliantAttribute(true)]
[module: System.CLSCompliantAttribute(true)]
class C
{
    static void M()
    {
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);

            // Verify full metadata contains expected rows.
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "C");

                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);

                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<MethodSymbol>("C.M"))));

                // Verify delta metadata contains expected rows.
                using (var md1 = diff1.GetMetadata())
                {
                    var readers = new[] { reader0, md1.Reader };
                    CheckNames(readers, md1.Reader.GetTypeDefNames());
                    CheckNames(readers, md1.Reader.GetMethodDefNames(), "M");
                    CheckEncLog(md1.Reader,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(7, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                        Row(2, TableIndex.MethodDef, EditAndContinueOperation.Default)); // C.M
                    CheckEncMap(md1.Reader,
                        Handle(7, TableIndex.TypeRef),
                        Handle(2, TableIndex.MethodDef),
                        Handle(2, TableIndex.AssemblyRef));
                }
            }
        }

        [Fact]
        public void OtherReferences()
        {
            var source0 =
@"delegate void D();
class C
{
    object F;
    object P { get { return null; } }
    event D E;
    void M()
    {
    }
}";
            var source1 =
@"delegate void D();
class C
{
    object F;
    object P { get { return null; } }
    event D E;
    void M()
    {
        object o;
        o = typeof(D);
        o = F;
        o = P;
        E += null;
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);

            // Verify full metadata contains expected rows.
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "D", "C");
                CheckNames(reader0, reader0.GetEventDefNames(), "E");
                CheckNames(reader0, reader0.GetFieldDefNames(), "F", "E");
                CheckNames(reader0, reader0.GetMethodDefNames(), ".ctor", "Invoke", "BeginInvoke", "EndInvoke", "get_P", "add_E", "remove_E", "M", ".ctor");
                CheckNames(reader0, reader0.GetPropertyDefNames(), "P");

                var method0 = compilation0.GetMember<MethodSymbol>("C.M");

                // Emit delta metadata.
                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);
                var method1 = compilation1.GetMember<MethodSymbol>("C.M");

                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));

                // Verify delta metadata contains expected rows.
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    CheckNames(readers, reader1.GetTypeDefNames());
                    CheckNames(readers, reader1.GetEventDefNames());
                    CheckNames(readers, reader1.GetFieldDefNames());
                    CheckNames(readers, reader1.GetMethodDefNames(), "M");
                    CheckNames(readers, reader1.GetPropertyDefNames());
                }
            }
        }

        [Fact]
        public void ArrayInitializer()
        {
            var source0 = @"
class C
{
    static void M()
    {
        int[] a = new[] { 1, 2, 3 };
    }
}";
            var source1 = @"
class C
{
    static void M()
    {
        int[] a = new[] { 1, 2, 3, 4 };
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(Parse(source0, "a.cs"), options: TestOptions.DebugDll);
            var compilation1 = compilation0.RemoveAllSyntaxTrees().AddSyntaxTrees(Parse(source1, "a.cs"));

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);

            var generation0 = EmitBaseline.CreateInitialBaseline(
                ModuleMetadata.CreateFromImage(bytes0),
                testData0.GetMethodData("C.M").EncDebugInfoProvider());

            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation0.GetMember("C.M"), compilation1.GetMember("C.M"))));

            using (var md1 = diff1.GetMetadata())
            {
                var reader1 = md1.Reader;
                CheckEncLog(reader1,
                    Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                    Row(12, TableIndex.TypeRef, EditAndContinueOperation.Default),
                    Row(13, TableIndex.TypeRef, EditAndContinueOperation.Default),
                    //Row(2, TableIndex.TypeSpec, EditAndContinueOperation.Default),
                    Row(2, TableIndex.StandAloneSig, EditAndContinueOperation.Default),
                    Row(1, TableIndex.MethodDef, EditAndContinueOperation.Default));
                CheckEncMap(reader1,
                    Handle(12, TableIndex.TypeRef),
                    Handle(13, TableIndex.TypeRef),
                    Handle(1, TableIndex.MethodDef),
                    Handle(2, TableIndex.StandAloneSig),
                    //Handle(2, TableIndex.TypeSpec),
                    Handle(2, TableIndex.AssemblyRef));
            }

            diff1.VerifyIL(
@"{
  // Code size       25 (0x19)
  .maxstack  4
  IL_0000:  nop
  IL_0001:  ldc.i4.4
  IL_0002:  newarr     0x0100000D
  IL_0007:  dup
  IL_0008:  ldc.i4.0
  IL_0009:  ldc.i4.1
  IL_000a:  stelem.i4
  IL_000b:  dup
  IL_000c:  ldc.i4.1
  IL_000d:  ldc.i4.2
  IL_000e:  stelem.i4
  IL_000f:  dup
  IL_0010:  ldc.i4.2
  IL_0011:  ldc.i4.3
  IL_0012:  stelem.i4
  IL_0013:  dup
  IL_0014:  ldc.i4.3
  IL_0015:  ldc.i4.4
  IL_0016:  stelem.i4
  IL_0017:  stloc.0
  IL_0018:  ret
}");

            diff1.VerifyPdb(new[] { 0x06000001 },
@"<symbols>
  <files>
    <file id=""1"" name=""a.cs"" language=""3f5162f8-07c6-11d3-9053-00c04fa302a1"" languageVendor=""994b45c4-e6e9-11d2-903f-00c04fa302a1"" documentType=""5a869d0b-6611-11d3-bd2a-0000f80849bd"" checkSumAlgorithmId=""ff1816ec-aa5e-4d10-87f7-6f4963833460"" checkSum=""15, 9B, 5B, 24, 28, 37,  2, 4F, D2, 2E, 40, DB, 1A, 89, 9F, 4D, 54, D5, 95, 89, "" />
  </files>
  <methods>
    <method token=""0x6000001"">
      <customDebugInfo version=""4"" count=""1"">
        <using version=""4"" kind=""UsingInfo"" size=""12"" namespaceCount=""1"">
          <namespace usingCount=""0"" />
        </using>
      </customDebugInfo>
      <sequencepoints total=""3"">
        <entry il_offset=""0x0"" start_row=""5"" start_column=""5"" end_row=""5"" end_column=""6"" file_ref=""1"" />
        <entry il_offset=""0x1"" start_row=""6"" start_column=""9"" end_row=""6"" end_column=""40"" file_ref=""1"" />
        <entry il_offset=""0x18"" start_row=""7"" start_column=""5"" end_row=""7"" end_column=""6"" file_ref=""1"" />
      </sequencepoints>
      <locals>
        <local name=""a"" il_index=""0"" il_start=""0x0"" il_end=""0x19"" attributes=""0"" />
      </locals>
      <scope startOffset=""0x0"" endOffset=""0x19"">
        <local name=""a"" il_index=""0"" il_start=""0x0"" il_end=""0x19"" attributes=""0"" />
      </scope>
    </method>
  </methods>
</symbols>");
        }

        [Fact]
        public void PInvokeModuleRefAndImplMap()
        {
            var source0 =
@"using System.Runtime.InteropServices;
class C
{
    [DllImport(""msvcrt.dll"")]
    public static extern int getchar();
}";
            var source1 =
@"using System.Runtime.InteropServices;
class C
{
    [DllImport(""msvcrt.dll"")]
    public static extern int getchar();
    [DllImport(""msvcrt.dll"")]
    public static extern int puts(string s);
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var bytes0 = compilation0.EmitToArray();
            var generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), EmptyLocalsProvider);

            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<MethodSymbol>("C.puts"))));

            using (var md1 = diff1.GetMetadata())
            {
                var reader1 = md1.Reader;
                CheckEncLog(reader1,
                    Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                    Row(2, TableIndex.ModuleRef, EditAndContinueOperation.Default),
                    Row(6, TableIndex.TypeRef, EditAndContinueOperation.Default),
                    Row(2, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                    Row(3, TableIndex.MethodDef, EditAndContinueOperation.Default),
                    Row(3, TableIndex.MethodDef, EditAndContinueOperation.AddParameter),
                    Row(1, TableIndex.Param, EditAndContinueOperation.Default),
                    Row(2, TableIndex.ImplMap, EditAndContinueOperation.Default));
                CheckEncMap(reader1,
                    Handle(6, TableIndex.TypeRef),
                    Handle(3, TableIndex.MethodDef),
                    Handle(1, TableIndex.Param),
                    Handle(2, TableIndex.ModuleRef),
                    Handle(2, TableIndex.ImplMap),
                    Handle(2, TableIndex.AssemblyRef));
            }
        }

        /// <summary>
        /// ClassLayout and FieldLayout tables.
        /// </summary>
        [Fact]
        public void ClassAndFieldLayout()
        {
            var source0 =
@"using System.Runtime.InteropServices;
[StructLayout(LayoutKind.Explicit, Pack=2)]
class A
{
    [FieldOffset(0)]internal byte F;
    [FieldOffset(2)]internal byte G;
}";
            var source1 =
@"using System.Runtime.InteropServices;
[StructLayout(LayoutKind.Explicit, Pack=2)]
class A
{
    [FieldOffset(0)]internal byte F;
    [FieldOffset(2)]internal byte G;
}
[StructLayout(LayoutKind.Explicit, Pack=4)]
class B
{
    [FieldOffset(0)]internal short F;
    [FieldOffset(4)]internal short G;
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var bytes0 = compilation0.EmitToArray();
            var generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), EmptyLocalsProvider);

            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<NamedTypeSymbol>("B"))));

            using (var md1 = diff1.GetMetadata())
            {
                var reader1 = md1.Reader;
                CheckEncLog(reader1,
                    Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                    Row(5, TableIndex.MemberRef, EditAndContinueOperation.Default),
                    Row(6, TableIndex.TypeRef, EditAndContinueOperation.Default),
                    Row(3, TableIndex.TypeDef, EditAndContinueOperation.Default),
                    Row(3, TableIndex.TypeDef, EditAndContinueOperation.AddField),
                    Row(3, TableIndex.Field, EditAndContinueOperation.Default),
                    Row(3, TableIndex.TypeDef, EditAndContinueOperation.AddField),
                    Row(4, TableIndex.Field, EditAndContinueOperation.Default),
                    Row(3, TableIndex.TypeDef, EditAndContinueOperation.AddMethod),
                    Row(2, TableIndex.MethodDef, EditAndContinueOperation.Default),
                    Row(2, TableIndex.ClassLayout, EditAndContinueOperation.Default),
                    Row(3, TableIndex.FieldLayout, EditAndContinueOperation.Default),
                    Row(4, TableIndex.FieldLayout, EditAndContinueOperation.Default));
                CheckEncMap(reader1,
                    Handle(6, TableIndex.TypeRef),
                    Handle(3, TableIndex.TypeDef),
                    Handle(3, TableIndex.Field),
                    Handle(4, TableIndex.Field),
                    Handle(2, TableIndex.MethodDef),
                    Handle(5, TableIndex.MemberRef),
                    Handle(2, TableIndex.ClassLayout),
                    Handle(3, TableIndex.FieldLayout),
                    Handle(4, TableIndex.FieldLayout),
                    Handle(2, TableIndex.AssemblyRef));
            }
        }

        [Fact]
        public void NamespacesAndOverloads()
        {
            var compilation0 = CreateCompilationWithMscorlib(options: TestOptions.DebugDll, text:
@"class C { }
namespace N
{
    class C { }
}
namespace M
{
    class C
    {
        void M1(N.C o) { }
        void M1(M.C o) { }
        void M2(N.C a, M.C b, global::C c)
        {
            M1(a);
        }
    }
}");

            var method0 = compilation0.GetMember<MethodSymbol>("M.C.M2");

            var bytes0 = compilation0.EmitToArray();
            var generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), EmptyLocalsProvider);

            var compilation1 = compilation0.WithSource(@"
class C { }
namespace N
{
    class C { }
}
namespace M
{
    class C
    {
        void M1(N.C o) { }
        void M1(M.C o) { }
        void M1(global::C o) { }
        void M2(N.C a, M.C b, global::C c)
        {
            M1(a);
            M1(b);
        }
    }
}");
            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMembers("M.C.M1")[2])));

            diff1.VerifyIL(
@"{
  // Code size        2 (0x2)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ret
}");

            var compilation2 = compilation1.WithSource(@"
class C { }
namespace N
{
    class C { }
}
namespace M
{
    class C
    {
        void M1(N.C o) { }
        void M1(M.C o) { }
        void M1(global::C o) { }
        void M2(N.C a, M.C b, global::C c)
        {
            M1(a);
            M1(b);
            M1(c);
        }
    }
}");
            var diff2 = compilation2.EmitDifference(
                diff1.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation1.GetMember<MethodSymbol>("M.C.M2"),
                                                                        compilation2.GetMember<MethodSymbol>("M.C.M2"))));

            diff2.VerifyIL(
@"{
  // Code size       26 (0x1a)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  ldarg.1
  IL_0003:  call       0x06000002
  IL_0008:  nop
  IL_0009:  ldarg.0
  IL_000a:  ldarg.2
  IL_000b:  call       0x06000003
  IL_0010:  nop
  IL_0011:  ldarg.0
  IL_0012:  ldarg.3
  IL_0013:  call       0x06000007
  IL_0018:  nop
  IL_0019:  ret
}");
        }

        [Fact]
        public void TypesAndOverloads()
        {
            const string source =
@"using System;
struct A<T>
{
    internal class B<U> { }
}
class B { }
class C
{
    static void M(A<B>.B<object> a)
    {
        M(a);
        M((A<B>.B<B>)null);
    }
    static void M(A<B>.B<B> a)
    {
        M(a);
        M((A<B>.B<object>)null);
    }
    static void M(A<B> a)
    {
        M(a);
        M((A<B>?)a);
    }
    static void M(Nullable<A<B>> a)
    {
        M(a);
        M(a.Value);
    }
    unsafe static void M(int* p)
    {
        M(p);
        M((byte*)p);
    }
    unsafe static void M(byte* p)
    {
        M(p);
        M((int*)p);
    }
    static void M(B[][] b)
    {
        M(b);
        M((object[][])b);
    }
    static void M(object[][] b)
    {
        M(b);
        M((B[][])b);
    }
    static void M(A<B[]>.B<object> b)
    {
        M(b);
        M((A<B[, ,]>.B<object>)null);
    }
    static void M(A<B[, ,]>.B<object> b)
    {
        M(b);
        M((A<B[]>.B<object>)null);
    }
    static void M(dynamic d)
    {
        M(d);
        M((dynamic[])d);
    }
    static void M(dynamic[] d)
    {
        M(d);
        M((dynamic)d);
    }
    static void M<T>(A<int>.B<T> t) where T : B
    {
        M(t);
        M((A<double>.B<int>)null);
    }
    static void M<T>(A<double>.B<T> t) where T : struct
    {
        M(t);
        M((A<int>.B<B>)null);
    }
}";
            var options = TestOptions.UnsafeDebugDll;
            var compilation0 = CreateCompilationWithMscorlib(source, options: options, references: new[] { SystemCoreRef, CSharpRef });
            var bytes0 = compilation0.EmitToArray();
            var generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), EmptyLocalsProvider);

            var n = compilation0.GetMembers("C.M").Length;
            Assert.Equal(n, 14);

            //static void M(A<B>.B<object> a)
            //{
            //    M(a);
            //    M((A<B>.B<B>)null);
            //}
            var compilation1 = compilation0.WithSource(source);
            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation0.GetMembers("C.M")[0], compilation1.GetMembers("C.M")[0])));

            diff1.VerifyIL(
@"{
  // Code size       16 (0x10)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  call       0x06000002
  IL_0007:  nop
  IL_0008:  ldnull
  IL_0009:  call       0x06000003
  IL_000e:  nop
  IL_000f:  ret
}");

            //static void M(A<B>.B<B> a)
            //{
            //    M(a);
            //    M((A<B>.B<object>)null);
            //}
            var compilation2 = compilation1.WithSource(source);
            var diff2 = compilation2.EmitDifference(
                diff1.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation1.GetMembers("C.M")[1], compilation2.GetMembers("C.M")[1])));

            diff2.VerifyIL(
@"{
  // Code size       16 (0x10)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  call       0x06000003
  IL_0007:  nop
  IL_0008:  ldnull
  IL_0009:  call       0x06000002
  IL_000e:  nop
  IL_000f:  ret
}");

            //static void M(A<B> a)
            //{
            //    M(a);
            //    M((A<B>?)a);
            //}
            var compilation3 = compilation2.WithSource(source);
            var diff3 = compilation3.EmitDifference(
                diff2.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation2.GetMembers("C.M")[2], compilation3.GetMembers("C.M")[2])));

            diff3.VerifyIL(
@"{
  // Code size       21 (0x15)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  call       0x06000004
  IL_0007:  nop
  IL_0008:  ldarg.0
  IL_0009:  newobj     0x0A000016
  IL_000e:  call       0x06000005
  IL_0013:  nop
  IL_0014:  ret
}");

            //static void M(Nullable<A<B>> a)
            //{
            //    M(a);
            //    M(a.Value);
            //}
            var compilation4 = compilation3.WithSource(source);
            var diff4 = compilation4.EmitDifference(
                diff3.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation3.GetMembers("C.M")[3], compilation4.GetMembers("C.M")[3])));

            diff4.VerifyIL(
@"{
  // Code size       22 (0x16)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  call       0x06000005
  IL_0007:  nop
  IL_0008:  ldarga.s   V_0
  IL_000a:  call       0x0A000017
  IL_000f:  call       0x06000004
  IL_0014:  nop
  IL_0015:  ret
}");

            //unsafe static void M(int* p)
            //{
            //    M(p);
            //    M((byte*)p);
            //}
            var compilation5 = compilation4.WithSource(source);
            var diff5 = compilation5.EmitDifference(
                diff4.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation4.GetMembers("C.M")[4], compilation5.GetMembers("C.M")[4])));

            diff5.VerifyIL(
@"{
  // Code size       16 (0x10)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  call       0x06000006
  IL_0007:  nop
  IL_0008:  ldarg.0
  IL_0009:  call       0x06000007
  IL_000e:  nop
  IL_000f:  ret
}");

            //unsafe static void M(byte* p)
            //{
            //    M(p);
            //    M((int*)p);
            //}
            var compilation6 = compilation5.WithSource(source);
            var diff6 = compilation6.EmitDifference(
                diff5.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation5.GetMembers("C.M")[5], compilation6.GetMembers("C.M")[5])));

            diff6.VerifyIL(
@"{
  // Code size       16 (0x10)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  call       0x06000007
  IL_0007:  nop
  IL_0008:  ldarg.0
  IL_0009:  call       0x06000006
  IL_000e:  nop
  IL_000f:  ret
}");

            //static void M(B[][] b)
            //{
            //    M(b);
            //    M((object[][])b);
            //}
            var compilation7 = compilation6.WithSource(source);
            var diff7 = compilation7.EmitDifference(
                diff6.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation6.GetMembers("C.M")[6], compilation7.GetMembers("C.M")[6])));

            diff7.VerifyIL(
@"{
  // Code size       16 (0x10)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  call       0x06000008
  IL_0007:  nop
  IL_0008:  ldarg.0
  IL_0009:  call       0x06000009
  IL_000e:  nop
  IL_000f:  ret
}");

            //static void M(object[][] b)
            //{
            //    M(b);
            //    M((B[][])b);
            //}
            var compilation8 = compilation7.WithSource(source);
            var diff8 = compilation8.EmitDifference(
                diff7.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation7.GetMembers("C.M")[7], compilation8.GetMembers("C.M")[7])));

            diff8.VerifyIL(
@"{
  // Code size       21 (0x15)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  call       0x06000009
  IL_0007:  nop
  IL_0008:  ldarg.0
  IL_0009:  castclass  0x1B00000A
  IL_000e:  call       0x06000008
  IL_0013:  nop
  IL_0014:  ret
}");

            //static void M(A<B[]>.B<object> b)
            //{
            //    M(b);
            //    M((A<B[,,]>.B<object>)null);
            //}
            var compilation9 = compilation8.WithSource(source);
            var diff9 = compilation9.EmitDifference(
                diff8.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation8.GetMembers("C.M")[8], compilation9.GetMembers("C.M")[8])));

            diff9.VerifyIL(
@"{
  // Code size       16 (0x10)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  call       0x0600000A
  IL_0007:  nop
  IL_0008:  ldnull
  IL_0009:  call       0x0600000B
  IL_000e:  nop
  IL_000f:  ret
}");

            //static void M(A<B[,,]>.B<object> b)
            //{
            //    M(b);
            //    M((A<B[]>.B<object>)null);
            //}
            var compilation10 = compilation9.WithSource(source);
            var diff10 = compilation10.EmitDifference(
                diff9.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation9.GetMembers("C.M")[9], compilation10.GetMembers("C.M")[9])));

            diff10.VerifyIL(
@"{
  // Code size       16 (0x10)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  call       0x0600000B
  IL_0007:  nop
  IL_0008:  ldnull
  IL_0009:  call       0x0600000A
  IL_000e:  nop
  IL_000f:  ret
}");

            // TODO: dynamic
#if false
            //static void M(dynamic d)
            //{
            //    M(d);
            //    M((dynamic[])d);
            //}
            previousMethod = compilation.GetMembers("C.M")[10];
            compilation = compilation0.WithSource(source);
            generation = compilation.EmitDifference(
                generation,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, previousMethod, compilation.GetMembers("C.M")[10])),
@"{
  // Code size       16 (0x10)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  call       0x06000002
  IL_0007:  nop
  IL_0008:  ldnull
  IL_0009:  call       0x06000003
  IL_000e:  nop
  IL_000f:  ret
}");

            //static void M(dynamic[] d)
            //{
            //    M(d);
            //    M((dynamic)d);
            //}
            previousMethod = compilation.GetMembers("C.M")[11];
            compilation = compilation0.WithSource(source);
            generation = compilation.EmitDifference(
                generation,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, previousMethod, compilation.GetMembers("C.M")[11])),
@"{
  // Code size       16 (0x10)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  call       0x06000002
  IL_0007:  nop
  IL_0008:  ldnull
  IL_0009:  call       0x06000003
  IL_000e:  nop
  IL_000f:  ret
}");
#endif

            //static void M<T>(A<int>.B<T> t) where T : B
            //{
            //    M(t);
            //    M((A<double>.B<int>)null);
            //}
            var compilation11 = compilation10.WithSource(source);
            var diff11 = compilation11.EmitDifference(
                diff10.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation10.GetMembers("C.M")[12], compilation11.GetMembers("C.M")[12])));

            diff11.VerifyIL(
@"{
  // Code size       16 (0x10)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  call       0x2B000005
  IL_0007:  nop
  IL_0008:  ldnull
  IL_0009:  call       0x2B000006
  IL_000e:  nop
  IL_000f:  ret
}");

            //static void M<T>(A<double>.B<T> t) where T : struct
            //{
            //    M(t);
            //    M((A<int>.B<B>)null);
            //}
            var compilation12 = compilation11.WithSource(source);
            var diff12 = compilation12.EmitDifference(
                diff11.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation11.GetMembers("C.M")[13], compilation12.GetMembers("C.M")[13])));

            diff12.VerifyIL(
@"{
  // Code size       16 (0x10)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  call       0x2B000007
  IL_0007:  nop
  IL_0008:  ldnull
  IL_0009:  call       0x2B000008
  IL_000e:  nop
  IL_000f:  ret
}");
        }

        /// <summary>
        /// Types should be retained in deleted locals
        /// for correct alignment of remaining locals.
        /// </summary>
        [Fact]
        public void DeletedValueTypeLocal()
        {
            var source0 =
@"struct S1
{
    internal S1(int a, int b) { A = a; B = b; }
    internal int A;
    internal int B;
}
struct S2
{
    internal S2(int c) { C = c; }
    internal int C;
}
class C
{
    static void Main()
    {
        var x = new S1(1, 2);
        var y = new S2(3);
        System.Console.WriteLine(y.C);
    }
}";
            var source1 =
@"struct S1
{
    internal S1(int a, int b) { A = a; B = b; }
    internal int A;
    internal int B;
}
struct S2
{
    internal S2(int c) { C = c; }
    internal int C;
}
class C
{
    static void Main()
    {
        var y = new S2(3);
        System.Console.WriteLine(y.C);
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugExe);
            var compilation1 = compilation0.WithSource(source1);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            var methodData0 = testData0.GetMethodData("C.Main");
            var method0 = compilation0.GetMember<MethodSymbol>("C.Main");
            var generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), methodData0.EncDebugInfoProvider());
            testData0.GetMethodData("C.Main").VerifyIL(
@"
{
  // Code size       31 (0x1f)
  .maxstack  3
  .locals init (S1 V_0, //x
  S2 V_1) //y
  IL_0000:  nop
  IL_0001:  ldloca.s   V_0
  IL_0003:  ldc.i4.1
  IL_0004:  ldc.i4.2
  IL_0005:  call       ""S1..ctor(int, int)""
  IL_000a:  ldloca.s   V_1
  IL_000c:  ldc.i4.3
  IL_000d:  call       ""S2..ctor(int)""
  IL_0012:  ldloc.1
  IL_0013:  ldfld      ""int S2.C""
  IL_0018:  call       ""void System.Console.WriteLine(int)""
  IL_001d:  nop
  IL_001e:  ret
}");

            var method1 = compilation1.GetMember<MethodSymbol>("C.Main");
            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));
            diff1.VerifyIL("C.Main",
 @"{
  // Code size       22 (0x16)
  .maxstack  2
  .locals init ([unchanged] V_0,
  S2 V_1) //y
  IL_0000:  nop
  IL_0001:  ldloca.s   V_1
  IL_0003:  ldc.i4.3
  IL_0004:  call       ""S2..ctor(int)""
  IL_0009:  ldloc.1
  IL_000a:  ldfld      ""int S2.C""
  IL_000f:  call       ""void System.Console.WriteLine(int)""
  IL_0014:  nop
  IL_0015:  ret
}");
        }

        /// <summary>
        /// Instance and static constructors synthesized for
        /// PrivateImplementationDetails should not be
        /// generated for delta.
        /// </summary>
        [Fact]
        public void PrivateImplementationDetails()
        {
            var source =
@"class C
{
    static int[] F = new int[] { 1, 2, 3 };
    int[] G = new int[] { 4, 5, 6 };
    int M(int index)
    {
        return F[index] + G[index];
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                var typeNames = new[] { reader0 }.GetStrings(reader0.GetTypeDefNames());
                Assert.NotNull(typeNames.FirstOrDefault(n => n.StartsWith("<PrivateImplementationDetails>")));
            }

            var methodData0 = testData0.GetMethodData("C.M");
            var method0 = compilation0.GetMember<MethodSymbol>("C.M");
            var generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), methodData0.EncDebugInfoProvider());

            var method1 = compilation1.GetMember<MethodSymbol>("C.M");
            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));

            diff1.VerifyIL("C.M", @"
{
  // Code size       22 (0x16)
  .maxstack  3
  .locals init ([int] V_0,
                int V_1)
  IL_0000:  nop
  IL_0001:  ldsfld     ""int[] C.F""
  IL_0006:  ldarg.1
  IL_0007:  ldelem.i4
  IL_0008:  ldarg.0
  IL_0009:  ldfld      ""int[] C.G""
  IL_000e:  ldarg.1
  IL_000f:  ldelem.i4
  IL_0010:  add
  IL_0011:  stloc.1
  IL_0012:  br.s       IL_0014
  IL_0014:  ldloc.1
  IL_0015:  ret
}");
        }

        [WorkItem(780989, "DevDiv")]
        [WorkItem(829353, "DevDiv")]
        [Fact]
        public void PrivateImplementationDetails_ArrayInitializer_FromMetadata()
        {
            var source0 =
@"class C
{
    static void M()
    {
        int[] a = { 1, 2, 3 };
        System.Console.WriteLine(a[0]);
    }
}";
            var source1 =
@"class C
{
    static void M()
    {
        int[] a = { 1, 2, 3 };
        System.Console.WriteLine(a[1]);
    }
}";
            var source2 =
@"class C
{
    static void M()
    {
        int[] a = { 4, 5, 6, 7, 8, 9, 10 };
        System.Console.WriteLine(a[1]);
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation1.WithSource(source2);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            var methodData0 = testData0.GetMethodData("C.M");

            methodData0.VerifyIL(
@"{
  // Code size       29 (0x1d)
  .maxstack  3
  .locals init (int[] V_0) //a
  IL_0000:  nop
  IL_0001:  ldc.i4.3
  IL_0002:  newarr     ""int""
  IL_0007:  dup
  IL_0008:  ldtoken    ""<PrivateImplementationDetails>.__StaticArrayInitTypeSize=12 <PrivateImplementationDetails>.$$method0x6000001-E429CCA3F703A39CC5954A6572FEC9086135B34E""
  IL_000d:  call       ""void System.Runtime.CompilerServices.RuntimeHelpers.InitializeArray(System.Array, System.RuntimeFieldHandle)""
  IL_0012:  stloc.0
  IL_0013:  ldloc.0
  IL_0014:  ldc.i4.0
  IL_0015:  ldelem.i4
  IL_0016:  call       ""void System.Console.WriteLine(int)""
  IL_001b:  nop
  IL_001c:  ret
}");

            var method0 = compilation0.GetMember<MethodSymbol>("C.M");
            var generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), methodData0.EncDebugInfoProvider());

            var method1 = compilation1.GetMember<MethodSymbol>("C.M");

            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));

            diff1.VerifyIL("C.M",
@"{
  // Code size       30 (0x1e)
  .maxstack  4
  .locals init (int[] V_0) //a
  IL_0000:  nop
  IL_0001:  ldc.i4.3
  IL_0002:  newarr     ""int""
  IL_0007:  dup
  IL_0008:  ldc.i4.0
  IL_0009:  ldc.i4.1
  IL_000a:  stelem.i4
  IL_000b:  dup
  IL_000c:  ldc.i4.1
  IL_000d:  ldc.i4.2
  IL_000e:  stelem.i4
  IL_000f:  dup
  IL_0010:  ldc.i4.2
  IL_0011:  ldc.i4.3
  IL_0012:  stelem.i4
  IL_0013:  stloc.0
  IL_0014:  ldloc.0
  IL_0015:  ldc.i4.1
  IL_0016:  ldelem.i4
  IL_0017:  call       ""void System.Console.WriteLine(int)""
  IL_001c:  nop
  IL_001d:  ret
}");

            var method2 = compilation2.GetMember<MethodSymbol>("C.M");

            var diff2 = compilation2.EmitDifference(
                diff1.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method1, method2, GetEquivalentNodesMap(method2, method1), preserveLocalVariables: true)));

            diff2.VerifyIL("C.M",
@"{
  // Code size       48 (0x30)
  .maxstack  4
  .locals init ([unchanged] V_0,
  int[] V_1) //a
  IL_0000:  nop
  IL_0001:  ldc.i4.7
  IL_0002:  newarr     ""int""
  IL_0007:  dup
  IL_0008:  ldc.i4.0
  IL_0009:  ldc.i4.4
  IL_000a:  stelem.i4
  IL_000b:  dup
  IL_000c:  ldc.i4.1
  IL_000d:  ldc.i4.5
  IL_000e:  stelem.i4
  IL_000f:  dup
  IL_0010:  ldc.i4.2
  IL_0011:  ldc.i4.6
  IL_0012:  stelem.i4
  IL_0013:  dup
  IL_0014:  ldc.i4.3
  IL_0015:  ldc.i4.7
  IL_0016:  stelem.i4
  IL_0017:  dup
  IL_0018:  ldc.i4.4
  IL_0019:  ldc.i4.8
  IL_001a:  stelem.i4
  IL_001b:  dup
  IL_001c:  ldc.i4.5
  IL_001d:  ldc.i4.s   9
  IL_001f:  stelem.i4
  IL_0020:  dup
  IL_0021:  ldc.i4.6
  IL_0022:  ldc.i4.s   10
  IL_0024:  stelem.i4
  IL_0025:  stloc.1
  IL_0026:  ldloc.1
  IL_0027:  ldc.i4.1
  IL_0028:  ldelem.i4
  IL_0029:  call       ""void System.Console.WriteLine(int)""
  IL_002e:  nop
  IL_002f:  ret
}");
        }

        [WorkItem(780989, "DevDiv")]
        [WorkItem(829353, "DevDiv")]
        [Fact]
        public void PrivateImplementationDetails_ArrayInitializer_FromSource()
        {
            // PrivateImplementationDetails not needed initially.
            var source0 =
@"class C
{
    static object F1() { return null; }
    static object F2() { return null; }
    static object F3() { return null; }
    static object F4() { return null; }
}";
            var source1 =
@"class C
{
    static object F1() { return new[] { 1, 2, 3 }; }
    static object F2() { return new[] { 4, 5, 6 }; }
    static object F3() { return null; }
    static object F4() { return new[] { 7, 8, 9 }; }
}";
            var source2 =
@"class C
{
    static object F1() { return new[] { 1, 2, 3 } ?? new[] { 10, 11, 12 }; }
    static object F2() { return new[] { 4, 5, 6 }; }
    static object F3() { return new[] { 13, 14, 15 }; }
    static object F4() { return new[] { 7, 8, 9 }; }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation1.WithSource(source2);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            var generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), EmptyLocalsProvider);

            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(
                    new SemanticEdit(SemanticEditKind.Update, compilation0.GetMember<MethodSymbol>("C.F1"), compilation1.GetMember<MethodSymbol>("C.F1")),
                    new SemanticEdit(SemanticEditKind.Update, compilation0.GetMember<MethodSymbol>("C.F2"), compilation1.GetMember<MethodSymbol>("C.F2")),
                    new SemanticEdit(SemanticEditKind.Update, compilation0.GetMember<MethodSymbol>("C.F4"), compilation1.GetMember<MethodSymbol>("C.F4"))));

            diff1.VerifyIL("C.F1",
@"{
  // Code size       24 (0x18)
  .maxstack  4
  .locals init (object V_0)
  IL_0000:  nop
  IL_0001:  ldc.i4.3
  IL_0002:  newarr     ""int""
  IL_0007:  dup
  IL_0008:  ldc.i4.0
  IL_0009:  ldc.i4.1
  IL_000a:  stelem.i4
  IL_000b:  dup
  IL_000c:  ldc.i4.1
  IL_000d:  ldc.i4.2
  IL_000e:  stelem.i4
  IL_000f:  dup
  IL_0010:  ldc.i4.2
  IL_0011:  ldc.i4.3
  IL_0012:  stelem.i4
  IL_0013:  stloc.0
  IL_0014:  br.s       IL_0016
  IL_0016:  ldloc.0
  IL_0017:  ret
}");
            diff1.VerifyIL("C.F4",
@"{
  // Code size       25 (0x19)
  .maxstack  4
  .locals init (object V_0)
  IL_0000:  nop
  IL_0001:  ldc.i4.3
  IL_0002:  newarr     ""int""
  IL_0007:  dup
  IL_0008:  ldc.i4.0
  IL_0009:  ldc.i4.7
  IL_000a:  stelem.i4
  IL_000b:  dup
  IL_000c:  ldc.i4.1
  IL_000d:  ldc.i4.8
  IL_000e:  stelem.i4
  IL_000f:  dup
  IL_0010:  ldc.i4.2
  IL_0011:  ldc.i4.s   9
  IL_0013:  stelem.i4
  IL_0014:  stloc.0
  IL_0015:  br.s       IL_0017
  IL_0017:  ldloc.0
  IL_0018:  ret
}");
            var diff2 = compilation2.EmitDifference(
                diff1.NextGeneration,
                ImmutableArray.Create(
                    new SemanticEdit(SemanticEditKind.Update, compilation1.GetMember<MethodSymbol>("C.F1"), compilation2.GetMember<MethodSymbol>("C.F1")),
                    new SemanticEdit(SemanticEditKind.Update, compilation1.GetMember<MethodSymbol>("C.F3"), compilation2.GetMember<MethodSymbol>("C.F3"))));

            diff2.VerifyIL("C.F1",
@"{
  // Code size       49 (0x31)
  .maxstack  4
  .locals init (object V_0)
  IL_0000:  nop
  IL_0001:  ldc.i4.3
  IL_0002:  newarr     ""int""
  IL_0007:  dup
  IL_0008:  ldc.i4.0
  IL_0009:  ldc.i4.1
  IL_000a:  stelem.i4
  IL_000b:  dup
  IL_000c:  ldc.i4.1
  IL_000d:  ldc.i4.2
  IL_000e:  stelem.i4
  IL_000f:  dup
  IL_0010:  ldc.i4.2
  IL_0011:  ldc.i4.3
  IL_0012:  stelem.i4
  IL_0013:  dup
  IL_0014:  brtrue.s   IL_002c
  IL_0016:  pop
  IL_0017:  ldc.i4.3
  IL_0018:  newarr     ""int""
  IL_001d:  dup
  IL_001e:  ldc.i4.0
  IL_001f:  ldc.i4.s   10
  IL_0021:  stelem.i4
  IL_0022:  dup
  IL_0023:  ldc.i4.1
  IL_0024:  ldc.i4.s   11
  IL_0026:  stelem.i4
  IL_0027:  dup
  IL_0028:  ldc.i4.2
  IL_0029:  ldc.i4.s   12
  IL_002b:  stelem.i4
  IL_002c:  stloc.0
  IL_002d:  br.s       IL_002f
  IL_002f:  ldloc.0
  IL_0030:  ret
}");
            diff2.VerifyIL("C.F3",
@"{
  // Code size       27 (0x1b)
  .maxstack  4
  .locals init (object V_0)
  IL_0000:  nop
  IL_0001:  ldc.i4.3
  IL_0002:  newarr     ""int""
  IL_0007:  dup
  IL_0008:  ldc.i4.0
  IL_0009:  ldc.i4.s   13
  IL_000b:  stelem.i4
  IL_000c:  dup
  IL_000d:  ldc.i4.1
  IL_000e:  ldc.i4.s   14
  IL_0010:  stelem.i4
  IL_0011:  dup
  IL_0012:  ldc.i4.2
  IL_0013:  ldc.i4.s   15
  IL_0015:  stelem.i4
  IL_0016:  stloc.0
  IL_0017:  br.s       IL_0019
  IL_0019:  ldloc.0
  IL_001a:  ret
}");
        }

        /// <summary>
        /// Should not generate method for string switch since
        /// the CLR only allows adding private members.
        /// </summary>
        [WorkItem(834086, "DevDiv")]
        [Fact]
        public void PrivateImplementationDetails_ComputeStringHash()
        {
            var source =
@"class C
{
    static int F(string s)
    {
        switch (s)
        {
            case ""1"": return 1;
            case ""2"": return 2;
            case ""3"": return 3;
            case ""4"": return 4;
            case ""5"": return 5;
            case ""6"": return 6;
            case ""7"": return 7;
            default: return 0;
        }
    }
}";
            const string ComputeStringHashName = "$$method0x6000001-ComputeStringHash";
            var compilation0 = CreateCompilationWithMscorlib(source, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            var methodData0 = testData0.GetMethodData("C.F");
            var method0 = compilation0.GetMember<MethodSymbol>("C.F");
            var generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), methodData0.EncDebugInfoProvider());

            // Should have generated call to ComputeStringHash and
            // added the method to <PrivateImplementationDetails>.
            var actualIL0 = methodData0.GetMethodIL();
            Assert.True(actualIL0.Contains(ComputeStringHashName));

            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetMethodDefNames(), "F", ".ctor", ComputeStringHashName);

                var method1 = compilation1.GetMember<MethodSymbol>("C.F");
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));

                // Should not have generated call to ComputeStringHash nor
                // added the method to <PrivateImplementationDetails>.
                var actualIL1 = diff1.GetMethodIL("C.F");
                Assert.False(actualIL1.Contains(ComputeStringHashName));

                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    CheckNames(readers, reader1.GetMethodDefNames(), "F");
                }
            }
        }

        /// <summary>
        /// Unique ids should not conflict with ids
        /// from previous generation.
        /// </summary>
        [Fact(Skip = "TODO")]
        public void UniqueIds()
        {
            var source0 =
@"class C
{
    int F()
    {
        System.Func<int> f = () => 3;
        return f();
    }
    static int F(bool b)
    {
        System.Func<int> f = () => 1;
        System.Func<int> g = () => 2;
        return (b ? f : g)();
    }
}";
            var source1 =
@"class C
{
    int F()
    {
        System.Func<int> f = () => 3;
        return f();
    }
    static int F(bool b)
    {
        System.Func<int> f = () => 1;
        return f();
    }
}";
            var source2 =
@"class C
{
    int F()
    {
        System.Func<int> f = () => 3;
        return f();
    }
    static int F(bool b)
    {
        System.Func<int> g = () => 2;
        return g();
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation1.WithSource(source2);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            var generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), EmptyLocalsProvider);

            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation0.GetMembers("C.F")[1], compilation1.GetMembers("C.F")[1])));

            diff1.VerifyIL("C.F",
@"{
  // Code size       40 (0x28)
  .maxstack  2
  .locals init (System.Func<int> V_0, //f
  int V_1)
  IL_0000:  nop
  IL_0001:  ldsfld     ""System.Func<int> C.CS$<>9__CachedAnonymousMethodDelegate6""
  IL_0006:  dup
  IL_0007:  brtrue.s   IL_001c
  IL_0009:  pop
  IL_000a:  ldnull
  IL_000b:  ldftn      ""int C.<F>b__5()""
  IL_0011:  newobj     ""System.Func<int>..ctor(object, System.IntPtr)""
  IL_0016:  dup
  IL_0017:  stsfld     ""System.Func<int> C.CS$<>9__CachedAnonymousMethodDelegate6""
  IL_001c:  stloc.0
  IL_001d:  ldloc.0
  IL_001e:  callvirt   ""int System.Func<int>.Invoke()""
  IL_0023:  stloc.1
  IL_0024:  br.s       IL_0026
  IL_0026:  ldloc.1
  IL_0027:  ret
}");

            var diff2 = compilation2.EmitDifference(
                diff1.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, compilation1.GetMembers("C.F")[1], compilation2.GetMembers("C.F")[1])));

            diff2.VerifyIL("C.F",
@"{
  // Code size       40 (0x28)
  .maxstack  2
  .locals init (System.Func<int> V_0, //g
  int V_1)
  IL_0000:  nop
  IL_0001:  ldsfld     ""System.Func<int> C.CS$<>9__CachedAnonymousMethodDelegate8""
  IL_0006:  dup
  IL_0007:  brtrue.s   IL_001c
  IL_0009:  pop
  IL_000a:  ldnull
  IL_000b:  ldftn      ""int C.<F>b__7()""
  IL_0011:  newobj     ""System.Func<int>..ctor(object, System.IntPtr)""
  IL_0016:  dup
  IL_0017:  stsfld     ""System.Func<int> C.CS$<>9__CachedAnonymousMethodDelegate8""
  IL_001c:  stloc.0
  IL_001d:  ldloc.0
  IL_001e:  callvirt   ""int System.Func<int>.Invoke()""
  IL_0023:  stloc.1
  IL_0024:  br.s       IL_0026
  IL_0026:  ldloc.1
  IL_0027:  ret
}");
        }

        /// <summary>
        /// Avoid adding references from method bodies
        /// other than the changed methods.
        /// </summary>
        [Fact]
        public void ReferencesInIL()
        {
            var source0 =
@"class C
{
    static void F() { System.Console.WriteLine(1); }
    static void G() { System.Console.WriteLine(2); }
}";
            var source1 =
@"class C
{
    static void F() { System.Console.WriteLine(1); }
    static void G() { System.Console.Write(2); }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);

            // Verify full metadata contains expected rows.
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "C");
                CheckNames(reader0, reader0.GetMethodDefNames(), "F", "G", ".ctor");
                CheckNames(reader0, reader0.GetMemberRefNames(), ".ctor", ".ctor", ".ctor", "WriteLine", ".ctor");

                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);
                var method0 = compilation0.GetMember<MethodSymbol>("C.G");
                var method1 = compilation1.GetMember<MethodSymbol>("C.G");

                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(
                        SemanticEditKind.Update,
                        method0,
                        method1,
                        GetEquivalentNodesMap(method1, method0),
                        preserveLocalVariables: true)));

                // "Write" should be included in string table, but "WriteLine" should not.
                Assert.True(diff1.MetadataDelta.IsIncluded("Write"));
                Assert.False(diff1.MetadataDelta.IsIncluded("WriteLine"));
            }
        }

        /// <summary>
        /// Local slots must be preserved based on signature.
        /// </summary>
        [Fact]
        public void PreserveLocalSlots()
        {
            var source0 =
@"class A<T> { }
class B : A<B>
{
    static B F()
    {
        return null;
    }
    static void M(object o)
    {
        object x = F();
        A<B> y = F();
        object z = F();
        M(x);
        M(y);
        M(z);
    }
    static void N()
    {
        object a = F();
        object b = F();
        M(a);
        M(b);
    }
}";
            var methodNames0 = new[] { "A<T>..ctor", "B.F", "B.M", "B.N" };

            var source1 =
@"class A<T> { }
class B : A<B>
{
    static B F()
    {
        return null;
    }
    static void M(object o)
    {
        B z = F();
        A<B> y = F();
        object w = F();
        M(w);
        M(y);
    }
    static void N()
    {
        object a = F();
        object b = F();
        M(a);
        M(b);
    }
}";
            var source2 =
@"class A<T> { }
class B : A<B>
{
    static B F()
    {
        return null;
    }
    static void M(object o)
    {
        object x = F();
        B z = F();
        M(x);
        M(z);
    }
    static void N()
    {
        object a = F();
        object b = F();
        M(a);
        M(b);
    }
}";
            var source3 =
@"class A<T> { }
class B : A<B>
{
    static B F()
    {
        return null;
    }
    static void M(object o)
    {
        object x = F();
        B z = F();
        M(x);
        M(z);
    }
    static void N()
    {
        object c = F();
        object b = F();
        M(c);
        M(b);
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation1.WithSource(source2);
            var compilation3 = compilation2.WithSource(source3);

            var method0 = compilation0.GetMember<MethodSymbol>("B.M");
            var methodN = compilation0.GetMember<MethodSymbol>("B.N");

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            var generation0 = EmitBaseline.CreateInitialBaseline(
                ModuleMetadata.CreateFromImage(bytes0), 
                m => testData0.GetMethodData(methodNames0[MetadataTokens.GetRowNumber(m) - 1]).GetEncDebugInfo());

            #region Gen1 

            var method1 = compilation1.GetMember<MethodSymbol>("B.M");
            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));

            diff1.VerifyIL(
@"{
  // Code size       36 (0x24)
  .maxstack  1
  IL_0000:  nop       
  IL_0001:  call       0x06000002
  IL_0006:  stloc.3   
  IL_0007:  call       0x06000002
  IL_000c:  stloc.1   
  IL_000d:  call       0x06000002
  IL_0012:  stloc.s    V_4
  IL_0014:  ldloc.s    V_4
  IL_0016:  call       0x06000003
  IL_001b:  nop       
  IL_001c:  ldloc.1   
  IL_001d:  call       0x06000003
  IL_0022:  nop       
  IL_0023:  ret       
}");
            diff1.VerifyPdb(new[] { 0x06000001, 0x06000002, 0x06000003, 0x06000004 }, @"
<symbols>
  <methods>
    <method token=""0x6000003"">
      <customDebugInfo version=""4"" count=""1"">
        <using version=""4"" kind=""UsingInfo"" size=""12"" namespaceCount=""1"">
          <namespace usingCount=""0"" />
        </using>
      </customDebugInfo>
      <sequencepoints total=""7"">
        <entry il_offset=""0x0"" start_row=""9"" start_column=""5"" end_row=""9"" end_column=""6"" file_ref=""0"" />
        <entry il_offset=""0x1"" start_row=""10"" start_column=""9"" end_row=""10"" end_column=""19"" file_ref=""0"" />
        <entry il_offset=""0x7"" start_row=""11"" start_column=""9"" end_row=""11"" end_column=""22"" file_ref=""0"" />
        <entry il_offset=""0xd"" start_row=""12"" start_column=""9"" end_row=""12"" end_column=""24"" file_ref=""0"" />
        <entry il_offset=""0x14"" start_row=""13"" start_column=""9"" end_row=""13"" end_column=""14"" file_ref=""0"" />
        <entry il_offset=""0x1c"" start_row=""14"" start_column=""9"" end_row=""14"" end_column=""14"" file_ref=""0"" />
        <entry il_offset=""0x23"" start_row=""15"" start_column=""5"" end_row=""15"" end_column=""6"" file_ref=""0"" />
      </sequencepoints>
      <locals>
        <local name=""z"" il_index=""3"" il_start=""0x0"" il_end=""0x24"" attributes=""0"" />
        <local name=""y"" il_index=""1"" il_start=""0x0"" il_end=""0x24"" attributes=""0"" />
        <local name=""w"" il_index=""4"" il_start=""0x0"" il_end=""0x24"" attributes=""0"" />
      </locals>
      <scope startOffset=""0x0"" endOffset=""0x24"">
        <local name=""z"" il_index=""3"" il_start=""0x0"" il_end=""0x24"" attributes=""0"" />
        <local name=""y"" il_index=""1"" il_start=""0x0"" il_end=""0x24"" attributes=""0"" />
        <local name=""w"" il_index=""4"" il_start=""0x0"" il_end=""0x24"" attributes=""0"" />
      </scope>
    </method>
  </methods>
</symbols>");

            #endregion

            #region Gen2 

            var method2 = compilation2.GetMember<MethodSymbol>("B.M");
            var diff2 = compilation2.EmitDifference(
                diff1.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method1, method2, GetEquivalentNodesMap(method2, method1), preserveLocalVariables: true)));

            diff2.VerifyIL(
@"{
  // Code size       30 (0x1e)
  .maxstack  1
  IL_0000:  nop
  IL_0001:  call       0x06000002
  IL_0006:  stloc.s    V_5
  IL_0008:  call       0x06000002
  IL_000d:  stloc.3
  IL_000e:  ldloc.s    V_5
  IL_0010:  call       0x06000003
  IL_0015:  nop
  IL_0016:  ldloc.3
  IL_0017:  call       0x06000003
  IL_001c:  nop
  IL_001d:  ret
}");

            diff2.VerifyPdb(new[] { 0x06000001, 0x06000002, 0x06000003, 0x06000004 }, @"
<symbols>
  <methods>
    <method token=""0x6000003"">
      <customDebugInfo version=""4"" count=""1"">
        <using version=""4"" kind=""UsingInfo"" size=""12"" namespaceCount=""1"">
          <namespace usingCount=""0"" />
        </using>
      </customDebugInfo>
      <sequencepoints total=""6"">
        <entry il_offset=""0x0"" start_row=""9"" start_column=""5"" end_row=""9"" end_column=""6"" file_ref=""0"" />
        <entry il_offset=""0x1"" start_row=""10"" start_column=""9"" end_row=""10"" end_column=""24"" file_ref=""0"" />
        <entry il_offset=""0x8"" start_row=""11"" start_column=""9"" end_row=""11"" end_column=""19"" file_ref=""0"" />
        <entry il_offset=""0xe"" start_row=""12"" start_column=""9"" end_row=""12"" end_column=""14"" file_ref=""0"" />
        <entry il_offset=""0x16"" start_row=""13"" start_column=""9"" end_row=""13"" end_column=""14"" file_ref=""0"" />
        <entry il_offset=""0x1d"" start_row=""14"" start_column=""5"" end_row=""14"" end_column=""6"" file_ref=""0"" />
      </sequencepoints>
      <locals>
        <local name=""x"" il_index=""5"" il_start=""0x0"" il_end=""0x1e"" attributes=""0"" />
        <local name=""z"" il_index=""3"" il_start=""0x0"" il_end=""0x1e"" attributes=""0"" />
      </locals>
      <scope startOffset=""0x0"" endOffset=""0x1e"">
        <local name=""x"" il_index=""5"" il_start=""0x0"" il_end=""0x1e"" attributes=""0"" />
        <local name=""z"" il_index=""3"" il_start=""0x0"" il_end=""0x1e"" attributes=""0"" />
      </scope>
    </method>
  </methods>
</symbols>");

            #endregion

            #region Gen3

            // Modify different method. (Previous generations
            // have not referenced method.)
            method2 = compilation2.GetMember<MethodSymbol>("B.N");
            var method3 = compilation3.GetMember<MethodSymbol>("B.N");
            var diff3 = compilation3.EmitDifference(
                diff2.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method2, method3, GetEquivalentNodesMap(method3, method2), preserveLocalVariables: true)));

            diff3.VerifyIL(
@"{
  // Code size       28 (0x1c)
  .maxstack  1
  IL_0000:  nop
  IL_0001:  call       0x06000002
  IL_0006:  stloc.2
  IL_0007:  call       0x06000002
  IL_000c:  stloc.1
  IL_000d:  ldloc.2
  IL_000e:  call       0x06000003
  IL_0013:  nop
  IL_0014:  ldloc.1
  IL_0015:  call       0x06000003
  IL_001a:  nop
  IL_001b:  ret
}");
            diff3.VerifyPdb(new[] { 0x06000001, 0x06000002, 0x06000003, 0x06000004 }, @"
<symbols>
  <methods>
    <method token=""0x6000004"">
      <customDebugInfo version=""4"" count=""1"">
        <using version=""4"" kind=""UsingInfo"" size=""12"" namespaceCount=""1"">
          <namespace usingCount=""0"" />
        </using>
      </customDebugInfo>
      <sequencepoints total=""6"">
        <entry il_offset=""0x0"" start_row=""16"" start_column=""5"" end_row=""16"" end_column=""6"" file_ref=""0"" />
        <entry il_offset=""0x1"" start_row=""17"" start_column=""9"" end_row=""17"" end_column=""24"" file_ref=""0"" />
        <entry il_offset=""0x7"" start_row=""18"" start_column=""9"" end_row=""18"" end_column=""24"" file_ref=""0"" />
        <entry il_offset=""0xd"" start_row=""19"" start_column=""9"" end_row=""19"" end_column=""14"" file_ref=""0"" />
        <entry il_offset=""0x14"" start_row=""20"" start_column=""9"" end_row=""20"" end_column=""14"" file_ref=""0"" />
        <entry il_offset=""0x1b"" start_row=""21"" start_column=""5"" end_row=""21"" end_column=""6"" file_ref=""0"" />
      </sequencepoints>
      <locals>
        <local name=""c"" il_index=""2"" il_start=""0x0"" il_end=""0x1c"" attributes=""0"" />
        <local name=""b"" il_index=""1"" il_start=""0x0"" il_end=""0x1c"" attributes=""0"" />
      </locals>
      <scope startOffset=""0x0"" endOffset=""0x1c"">
        <local name=""c"" il_index=""2"" il_start=""0x0"" il_end=""0x1c"" attributes=""0"" />
        <local name=""b"" il_index=""1"" il_start=""0x0"" il_end=""0x1c"" attributes=""0"" />
      </scope>
    </method>
  </methods>
</symbols>");

            #endregion
        }

        /// <summary>
        /// Preserve locals for method added after initial compilation.
        /// </summary>
        [Fact]
        public void PreserveLocalSlots_NewMethod()
        {
            var source0 =
@"class C
{
}";
            var source1 =
@"class C
{
    static void M()
    {
        var a = new object();
        var b = string.Empty;
    }
}";
            var source2 =
@"class C
{
    static void M()
    {
        var a = 1;
        var b = string.Empty;
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation1.WithSource(source2);

            var bytes0 = compilation0.EmitToArray();
            var generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), EmptyLocalsProvider);

            var method1 = compilation1.GetMember<MethodSymbol>("C.M");
            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Insert, null, method1, null, preserveLocalVariables: true)));

            var method2 = compilation2.GetMember<MethodSymbol>("C.M");
            var diff2 = compilation2.EmitDifference(
                diff1.NextGeneration,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method1, method2, GetEquivalentNodesMap(method2, method1), preserveLocalVariables: true)));
            diff2.VerifyIL("C.M",
@"{
  // Code size       10 (0xa)
  .maxstack  1
  .locals init ([object] V_0,
                string V_1, //b
                int V_2) //a
  IL_0000:  nop
  IL_0001:  ldc.i4.1
  IL_0002:  stloc.2
  IL_0003:  ldsfld     ""string string.Empty""
  IL_0008:  stloc.1
  IL_0009:  ret
}");
            diff2.VerifyPdb(new[] { 0x06000002 }, @"
<symbols>
  <methods>
    <method token=""0x6000002"">
      <customDebugInfo version=""4"" count=""1"">
        <using version=""4"" kind=""UsingInfo"" size=""12"" namespaceCount=""1"">
          <namespace usingCount=""0"" />
        </using>
      </customDebugInfo>
      <sequencepoints total=""4"">
        <entry il_offset=""0x0"" start_row=""4"" start_column=""5"" end_row=""4"" end_column=""6"" file_ref=""0"" />
        <entry il_offset=""0x1"" start_row=""5"" start_column=""9"" end_row=""5"" end_column=""19"" file_ref=""0"" />
        <entry il_offset=""0x3"" start_row=""6"" start_column=""9"" end_row=""6"" end_column=""30"" file_ref=""0"" />
        <entry il_offset=""0x9"" start_row=""7"" start_column=""5"" end_row=""7"" end_column=""6"" file_ref=""0"" />
      </sequencepoints>
      <locals>
        <local name=""a"" il_index=""2"" il_start=""0x0"" il_end=""0xa"" attributes=""0"" />
        <local name=""b"" il_index=""1"" il_start=""0x0"" il_end=""0xa"" attributes=""0"" />
      </locals>
      <scope startOffset=""0x0"" endOffset=""0xa"">
        <local name=""a"" il_index=""2"" il_start=""0x0"" il_end=""0xa"" attributes=""0"" />
        <local name=""b"" il_index=""1"" il_start=""0x0"" il_end=""0xa"" attributes=""0"" />
      </scope>
    </method>
  </methods>
</symbols>");
        }

        /// <summary>
        /// Local types should be retained, even if the local is no longer
        /// used by the method body, since there may be existing
        /// references to that slot, in a Watch window for instance.
        /// </summary>
        [WorkItem(843320, "DevDiv")]
        [Fact]
        public void PreserveLocalTypes()
        {
            var source0 =
@"class C
{
    static void Main()
    {
        var x = true;
        var y = x;
        System.Console.WriteLine(y);
    }
}";
            var source1 =
@"class C
{
    static void Main()
    {
        var x = ""A"";
        var y = x;
        System.Console.WriteLine(y);
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var method0 = compilation0.GetMember<MethodSymbol>("C.Main");
            var method1 = compilation1.GetMember<MethodSymbol>("C.Main");
            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);

            var generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), testData0.GetMethodData("C.Main").EncDebugInfoProvider());

            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));
            diff1.VerifyIL("C.Main", @"
{
  // Code size       17 (0x11)
  .maxstack  1
  .locals init ([bool] V_0,
                [bool] V_1,
                string V_2, //x
                string V_3) //y
  IL_0000:  nop
  IL_0001:  ldstr      ""A""
  IL_0006:  stloc.2
  IL_0007:  ldloc.2
  IL_0008:  stloc.3
  IL_0009:  ldloc.3
  IL_000a:  call       ""void System.Console.WriteLine(string)""
  IL_000f:  nop
  IL_0010:  ret
}");
        }

        /// <summary>
        /// Preserve locals if SemanticEdit.PreserveLocalVariables is set.
        /// </summary>
        [Fact]
        public void PreserveLocalVariablesFlag()
        {
            var source =
@"class C
{
    static System.IDisposable F() { return null; }
    static void M()
    {
        using (F()) { }
        using (var x = F()) { }
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            var method0 = compilation0.GetMember<MethodSymbol>("C.M");
            var generation0 = EmitBaseline.CreateInitialBaseline(
                ModuleMetadata.CreateFromImage(bytes0), 
                testData0.GetMethodData("C.M").EncDebugInfoProvider());

            var method1 = compilation1.GetMember<MethodSymbol>("C.M");
            var diff1a = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables: false)));

            diff1a.VerifyIL("C.M", @"
{
  // Code size       44 (0x2c)
  .maxstack  1
  .locals init (System.IDisposable V_0,
                System.IDisposable V_1) //x
  IL_0000:  nop
  IL_0001:  call       ""System.IDisposable C.F()""
  IL_0006:  stloc.0
  .try
  {
    IL_0007:  nop
    IL_0008:  nop
    IL_0009:  leave.s    IL_0016
  }
  finally
  {
    IL_000b:  ldloc.0
    IL_000c:  brfalse.s  IL_0015
    IL_000e:  ldloc.0
    IL_000f:  callvirt   ""void System.IDisposable.Dispose()""
    IL_0014:  nop
    IL_0015:  endfinally
  }
  IL_0016:  call       ""System.IDisposable C.F()""
  IL_001b:  stloc.1
  .try
  {
    IL_001c:  nop
    IL_001d:  nop
    IL_001e:  leave.s    IL_002b
  }
  finally
  {
    IL_0020:  ldloc.1
    IL_0021:  brfalse.s  IL_002a
    IL_0023:  ldloc.1
    IL_0024:  callvirt   ""void System.IDisposable.Dispose()""
    IL_0029:  nop
    IL_002a:  endfinally
  }
  IL_002b:  ret
}
");

            var diff1b = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables: true)));

            diff1b.VerifyIL("C.M",
@"{
  // Code size       44 (0x2c)
  .maxstack  1
  .locals init (System.IDisposable V_0,
                System.IDisposable V_1) //x
  IL_0000:  nop
  IL_0001:  call       ""System.IDisposable C.F()""
  IL_0006:  stloc.0
  .try
  {
    IL_0007:  nop
    IL_0008:  nop
    IL_0009:  leave.s    IL_0016
  }
  finally
  {
    IL_000b:  ldloc.0
    IL_000c:  brfalse.s  IL_0015
    IL_000e:  ldloc.0
    IL_000f:  callvirt   ""void System.IDisposable.Dispose()""
    IL_0014:  nop
    IL_0015:  endfinally
  }
  IL_0016:  call       ""System.IDisposable C.F()""
  IL_001b:  stloc.1
  .try
  {
    IL_001c:  nop
    IL_001d:  nop
    IL_001e:  leave.s    IL_002b
  }
  finally
  {
    IL_0020:  ldloc.1
    IL_0021:  brfalse.s  IL_002a
    IL_0023:  ldloc.1
    IL_0024:  callvirt   ""void System.IDisposable.Dispose()""
    IL_0029:  nop
    IL_002a:  endfinally
  }
  IL_002b:  ret
}");
        }

        [WorkItem(779531, "DevDiv")]
        [Fact]
        public void ChangeLocalType()
        {
            var source0 =
@"enum E { }
class C
{
    static void M1()
    {
        var x = default(E);
        var y = x;
        var z = default(E);
        System.Console.WriteLine(y);
    }
    static void M2()
    {
        var x = default(E);
        var y = x;
        var z = default(E);
        System.Console.WriteLine(y);
    }
}";
            // Change locals in one method to type added.
            var source1 =
@"enum E { }
class A { }
class C
{
    static void M1()
    {
        var x = default(A);
        var y = x;
        var z = default(E);
        System.Console.WriteLine(y);
    }
    static void M2()
    {
        var x = default(E);
        var y = x;
        var z = default(E);
        System.Console.WriteLine(y);
    }
}";
            // Change locals in another method.
            var source2 =
@"enum E { }
class A { }
class C
{
    static void M1()
    {
        var x = default(A);
        var y = x;
        var z = default(E);
        System.Console.WriteLine(y);
    }
    static void M2()
    {
        var x = default(A);
        var y = x;
        var z = default(E);
        System.Console.WriteLine(y);
    }
}";
            // Change locals in same method.
            var source3 =
@"enum E { }
class A { }
class C
{
    static void M1()
    {
        var x = default(A);
        var y = x;
        var z = default(E);
        System.Console.WriteLine(y);
    }
    static void M2()
    {
        var x = default(A);
        var y = x;
        var z = default(A);
        System.Console.WriteLine(y);
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation1.WithSource(source2);
            var compilation3 = compilation2.WithSource(source3);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            var methodData0 = testData0.GetMethodData("C.M1");
            var method0 = compilation0.GetMember<MethodSymbol>("C.M1");
            var generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), methodData0.EncDebugInfoProvider());

            var method1 = compilation1.GetMember<MethodSymbol>("C.M1");
            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(
                    new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<NamedTypeSymbol>("A")),
                    new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));

            diff1.VerifyIL("C.M1",
@"{
  // Code size       17 (0x11)
  .maxstack  1
  .locals init ([unchanged] V_0,
                [unchanged] V_1,
                E V_2, //z
                A V_3, //x
                A V_4) //y
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.3
  IL_0003:  ldloc.3
  IL_0004:  stloc.s    V_4
  IL_0006:  ldc.i4.0
  IL_0007:  stloc.2
  IL_0008:  ldloc.s    V_4
  IL_000a:  call       ""void System.Console.WriteLine(object)""
  IL_000f:  nop
  IL_0010:  ret
}");

            var method2 = compilation2.GetMember<MethodSymbol>("C.M2");
            var diff2 = compilation2.EmitDifference(
                diff1.NextGeneration,
                ImmutableArray.Create(
                    new SemanticEdit(SemanticEditKind.Update, method1, method2, GetEquivalentNodesMap(method2, method1), preserveLocalVariables: true)));

            diff2.VerifyIL("C.M2",
@"{
  // Code size       17 (0x11)
  .maxstack  1
  .locals init ([unchanged] V_0,
                [unchanged] V_1,
                E V_2, //z
                A V_3, //x
                A V_4) //y
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.3
  IL_0003:  ldloc.3
  IL_0004:  stloc.s    V_4
  IL_0006:  ldc.i4.0
  IL_0007:  stloc.2
  IL_0008:  ldloc.s    V_4
  IL_000a:  call       ""void System.Console.WriteLine(object)""
  IL_000f:  nop
  IL_0010:  ret
}");

            var method3 = compilation3.GetMember<MethodSymbol>("C.M2");
            var diff3 = compilation3.EmitDifference(
                diff2.NextGeneration,
                ImmutableArray.Create(
                    new SemanticEdit(SemanticEditKind.Update, method2, method3, GetEquivalentNodesMap(method3, method2), preserveLocalVariables: true)));

            diff3.VerifyIL("C.M2",
@"{
  // Code size       18 (0x12)
  .maxstack  1
  .locals init ([unchanged] V_0,
                [unchanged] V_1,
                [unchanged] V_2,
                A V_3, //x
                A V_4, //y
                A V_5) //z
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.3
  IL_0003:  ldloc.3
  IL_0004:  stloc.s    V_4
  IL_0006:  ldnull
  IL_0007:  stloc.s    V_5
  IL_0009:  ldloc.s    V_4
  IL_000b:  call       ""void System.Console.WriteLine(object)""
  IL_0010:  nop
  IL_0011:  ret
}");
        }

        /// <summary>
        /// Reuse existing anonymous types.
        /// </summary>
        [WorkItem(825903, "DevDiv")]
        [Fact]
        public void AnonymousTypes()
        {
            var source0 =
@"namespace N
{
    class A
    {
        static object F = new { A = 1, B = 2 };
    }
}
namespace M
{
    class B
    {
        static void M()
        {
            var x = new { B = 3, A = 4 };
            var y = x.A;
            var z = new { };
        }
    }
}";
            var source1 =
@"namespace N
{
    class A
    {
        static object F = new { A = 1, B = 2 };
    }
}
namespace M
{
    class B
    {
        static void M()
        {
            var x = new { B = 3, A = 4 };
            var y = new { A = x.A };
            var z = new { };
        }
    }
}";
            // Compile must be non-concurrent to ensure types are created in fixed order.
            var compOptions = TestOptions.DebugDll.WithConcurrentBuild(false);
            var compilation0 = CreateCompilationWithMscorlib(source0, options: compOptions);
            var compilation1 = compilation0.WithSource(source1);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);

            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var generation0 = EmitBaseline.CreateInitialBaseline(md0, testData0.GetMethodData("M.B.M").EncDebugInfoProvider());

                var method0 = compilation0.GetMember<MethodSymbol>("M.B.M");
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "<>f__AnonymousType0`2", "<>f__AnonymousType1`2", "<>f__AnonymousType2", "B", "A");

                var method1 = compilation1.GetMember<MethodSymbol>("M.B.M");
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    CheckNames(new[] { reader0, reader1 }, reader1.GetTypeDefNames(), "<>f__AnonymousType3`1"); // one additional type

                    diff1.VerifyIL("M.B.M", @"
{
  // Code size       28 (0x1c)
  .maxstack  2
  .locals init (<>f__AnonymousType1<int, int> V_0, //x
                [int] V_1,
                <>f__AnonymousType2 V_2, //z
                <>f__AnonymousType3<int> V_3) //y
  IL_0000:  nop
  IL_0001:  ldc.i4.3
  IL_0002:  ldc.i4.4
  IL_0003:  newobj     ""<>f__AnonymousType1<int, int>..ctor(int, int)""
  IL_0008:  stloc.0
  IL_0009:  ldloc.0
  IL_000a:  callvirt   ""int <>f__AnonymousType1<int, int>.A.get""
  IL_000f:  newobj     ""<>f__AnonymousType3<int>..ctor(int)""
  IL_0014:  stloc.3
  IL_0015:  newobj     ""<>f__AnonymousType2..ctor()""
  IL_001a:  stloc.2
  IL_001b:  ret
}");
                }
            }
        }

        /// <summary>
        /// Anonymous type names with module ids
        /// and gaps in indices.
        /// </summary>
        [Fact]
        public void AnonymousTypes_OtherTypeNames()
        {
            var ilSource =
@".assembly extern mscorlib { }
// Valid signature, although not sequential index
.class '<>f__AnonymousType2'<'<A>j__TPar', '<B>j__TPar'>
{
  .field public !'<A>j__TPar' A
  .field public !'<B>j__TPar' B
}
// Invalid signature, unexpected type parameter names
.class '<>f__AnonymousType1'<A, B>
{
  .field public !A A
  .field public !B B
}
// Module id, duplicate index
.class '<m>f__AnonymousType2`1'<'<A>j__TPar'>
{
  .field public !'<A>j__TPar' A
}
// Module id
.class '<m>f__AnonymousType3`1'<'<B>j__TPar'>
{
  .field public !'<B>j__TPar' B
}
.class public C
{
  .method public specialname rtspecialname instance void .ctor()
  {
    ret
  }
  .method public static object F()
  {
    ldnull
    ret
  }
}";
            var source0 =
@"class C
{
    static object F()
    {
        return 0;
    }
}";
            var source1 =
@"class C
{
    static object F()
    {
        var x = new { A = new object(), B = 1 };
        var y = new { A = x.A };
        return y;
    }
}";
            var metadata0 = (MetadataImageReference)CompileIL(ilSource);
            // Still need a compilation with source for the initial
            // generation - to get a MethodSymbol and syntax map.
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);

            var moduleMetadata0 = ((AssemblyMetadata)metadata0.GetMetadata()).GetModules()[0];
            var method0 = compilation0.GetMember<MethodSymbol>("C.F");
            var generation0 = EmitBaseline.CreateInitialBaseline(moduleMetadata0, m => default(EditAndContinueMethodDebugInformation));

            var method1 = compilation1.GetMember<MethodSymbol>("C.F");
            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));
            using (var md1 = diff1.GetMetadata())
            {
                diff1.VerifyIL("C.F",
    @"{
  // Code size       31 (0x1f)
  .maxstack  2
  .locals init (<>f__AnonymousType2<object, int> V_0, //x
  <>f__AnonymousType3<object> V_1, //y
  object V_2)
  IL_0000:  nop
  IL_0001:  newobj     ""object..ctor()""
  IL_0006:  ldc.i4.1
  IL_0007:  newobj     ""<>f__AnonymousType2<object, int>..ctor(object, int)""
  IL_000c:  stloc.0
  IL_000d:  ldloc.0
  IL_000e:  callvirt   ""object <>f__AnonymousType2<object, int>.A.get""
  IL_0013:  newobj     ""<>f__AnonymousType3<object>..ctor(object)""
  IL_0018:  stloc.1
  IL_0019:  ldloc.1
  IL_001a:  stloc.2
  IL_001b:  br.s       IL_001d
  IL_001d:  ldloc.2
  IL_001e:  ret
}");
            }
        }

        /// <summary>
        /// Update method with anonymous type that was
        /// not directly referenced in previous generation.
        /// </summary>
        [Fact]
        public void AnonymousTypes_SkipGeneration()
        {
            var source0 =
@"class A { }
class B
{
    static object F()
    {
        var x = new { A = 1 };
        return x.A;
    }
    static object G()
    {
        var x = 1;
        return x;
    }
}";
            var source1 =
@"class A { }
class B
{
    static object F()
    {
        var x = new { A = 1 };
        return x.A;
    }
    static object G()
    {
        var x = 1;
        return x + 1;
    }
}";
            var source2 =
@"class A { }
class B
{
    static object F()
    {
        var x = new { A = 1 };
        return x.A;
    }
    static object G()
    {
        var x = new { A = new A() };
        var y = new { B = 2 };
        return x.A;
    }
}";
            var source3 =
@"class A { }
class B
{
    static object F()
    {
        var x = new { A = 1 };
        return x.A;
    }
    static object G()
    {
        var x = new { A = new A() };
        var y = new { B = 3 };
        return y.B;
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation1.WithSource(source2);
            var compilation3 = compilation2.WithSource(source3);
                        
            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var generation0 = EmitBaseline.CreateInitialBaseline(
                    md0,
                    m =>
                    {
                        switch (md0.MetadataReader.GetString(md0.MetadataReader.GetMethodDefinition(m).Name))
                        {
                            case "F": return testData0.GetMethodData("B.F").GetEncDebugInfo();
                            case "G": return testData0.GetMethodData("B.G").GetEncDebugInfo();
                        }

                        return default(EditAndContinueMethodDebugInformation);
                    });

                var method0 = compilation0.GetMember<MethodSymbol>("B.G");
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "<>f__AnonymousType0`1", "A", "B");

                var method1 = compilation1.GetMember<MethodSymbol>("B.G");
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    CheckNames(new[] { reader0, reader1 }, reader1.GetTypeDefNames()); // no additional types
                    diff1.VerifyIL("B.G", @"
{
  // Code size       16 (0x10)
  .maxstack  2
  .locals init (int V_0, //x
           [object] V_1,
           object V_2)
  IL_0000:  nop       
  IL_0001:  ldc.i4.1  
  IL_0002:  stloc.0   
  IL_0003:  ldloc.0   
  IL_0004:  ldc.i4.1  
  IL_0005:  add       
  IL_0006:  box        ""int""
  IL_000b:  stloc.2   
  IL_000c:  br.s       IL_000e
  IL_000e:  ldloc.2   
  IL_000f:  ret       
}");

                    var method2 = compilation2.GetMember<MethodSymbol>("B.G");
                    var diff2 = compilation2.EmitDifference(
                        diff1.NextGeneration,
                        ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method1, method2, GetEquivalentNodesMap(method2, method1), preserveLocalVariables: true)));
                    using (var md2 = diff2.GetMetadata())
                    {
                        var reader2 = md2.Reader;
                        CheckNames(new[] { reader0, reader1, reader2 }, reader2.GetTypeDefNames(), "<>f__AnonymousType1`1"); // one additional type
                        diff2.VerifyIL("B.G", @"
{
  // Code size       33 (0x21)
  .maxstack  1
  .locals init ([int] V_0,
           [object] V_1,
           [object] V_2,
           <>f__AnonymousType0<A> V_3, //x
           <>f__AnonymousType1<int> V_4, //y
           object V_5)
  IL_0000:  nop       
  IL_0001:  newobj     ""A..ctor()""
  IL_0006:  newobj     ""<>f__AnonymousType0<A>..ctor(A)""
  IL_000b:  stloc.3   
  IL_000c:  ldc.i4.2  
  IL_000d:  newobj     ""<>f__AnonymousType1<int>..ctor(int)""
  IL_0012:  stloc.s    V_4
  IL_0014:  ldloc.3   
  IL_0015:  callvirt   ""A <>f__AnonymousType0<A>.A.get""
  IL_001a:  stloc.s    V_5
  IL_001c:  br.s       IL_001e
  IL_001e:  ldloc.s    V_5
  IL_0020:  ret       
}");

                        var method3 = compilation3.GetMember<MethodSymbol>("B.G");
                        var diff3 = compilation3.EmitDifference(
                            diff2.NextGeneration,
                            ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method2, method3, GetEquivalentNodesMap(method3, method2), preserveLocalVariables: true)));
                        using (var md3 = diff3.GetMetadata())
                        {
                            var reader3 = md3.Reader;
                            CheckNames(new[] { reader0, reader1, reader2, reader3 }, reader3.GetTypeDefNames()); // no additional types
                            diff3.VerifyIL("B.G",
    @"{
  // Code size       39 (0x27)
  .maxstack  1
  .locals init ([int] V_0,
           [object] V_1,
           [object] V_2,
           <>f__AnonymousType0<A> V_3, //x
           <>f__AnonymousType1<int> V_4, //y
           object V_5)
  IL_0000:  nop
  IL_0001:  newobj     ""A..ctor()""
  IL_0006:  newobj     ""<>f__AnonymousType0<A>..ctor(A)""
  IL_000b:  stloc.3
  IL_000c:  ldc.i4.3
  IL_000d:  newobj     ""<>f__AnonymousType1<int>..ctor(int)""
  IL_0012:  stloc.s    V_4
  IL_0014:  ldloc.s    V_4
  IL_0016:  callvirt   ""int <>f__AnonymousType1<int>.B.get""
  IL_001b:  box        ""int""
  IL_0020:  stloc.s    V_5
  IL_0022:  br.s       IL_0024
  IL_0024:  ldloc.s    V_5
  IL_0026:  ret
}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Update another method (without directly referencing
        /// anonymous type) after updating method with anonymous type.
        /// </summary>
        [Fact]
        public void AnonymousTypes_SkipGeneration_2()
        {
            var source0 =
@"class C
{
    static object F()
    {
        var x = new { A = 1 };
        return x.A;
    }
    static object G()
    {
        var x = 1;
        return x;
    }
}";
            var source1 =
@"class C
{
    static object F()
    {
        var x = new { A = 2, B = 3 };
        return x.A;
    }
    static object G()
    {
        var x = 1;
        return x;
    }
}";
            var source2 =
@"class C
{
    static object F()
    {
        var x = new { A = 2, B = 3 };
        return x.A;
    }
    static object G()
    {
        var x = 1;
        return x + 1;
    }
}";
            var source3 =
@"class C
{
    static object F()
    {
        var x = new { A = 2, B = 3 };
        return x.A;
    }
    static object G()
    {
        var x = new { A = (object)null };
        var y = new { A = 'a', B = 'b' };
        return x;
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation1.WithSource(source2);
            var compilation3 = compilation2.WithSource(source3);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var generation0 = EmitBaseline.CreateInitialBaseline(
                    md0,
                    m =>
                    {
                        switch (md0.MetadataReader.GetString(md0.MetadataReader.GetMethodDefinition(m).Name))
                        {
                            case "F": return testData0.GetMethodData("C.F").GetEncDebugInfo();
                            case "G": return testData0.GetMethodData("C.G").GetEncDebugInfo();
                        }

                        return default(EditAndContinueMethodDebugInformation);
                    });

                var method0F = compilation0.GetMember<MethodSymbol>("C.F");
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "<>f__AnonymousType0`1", "C");

                var method1F = compilation1.GetMember<MethodSymbol>("C.F");
                var method1G = compilation1.GetMember<MethodSymbol>("C.G");
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0F, method1F, GetEquivalentNodesMap(method1F, method0F), preserveLocalVariables: true)));
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    CheckNames(new[] { reader0, reader1 }, reader1.GetTypeDefNames(), "<>f__AnonymousType1`2"); // one additional type

                    var method2G = compilation2.GetMember<MethodSymbol>("C.G");
                    var diff2 = compilation2.EmitDifference(
                        diff1.NextGeneration,
                        ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method1G, method2G, GetEquivalentNodesMap(method2G, method1G), preserveLocalVariables: true)));
                    using (var md2 = diff2.GetMetadata())
                    {
                        var reader2 = md2.Reader;
                        CheckNames(new[] { reader0, reader1, reader2 }, reader2.GetTypeDefNames()); // no additional types

                        var method3G = compilation3.GetMember<MethodSymbol>("C.G");
                        var diff3 = compilation3.EmitDifference(
                            diff2.NextGeneration,
                            ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method2G, method3G, GetEquivalentNodesMap(method3G, method2G), preserveLocalVariables: true)));
                        using (var md3 = diff3.GetMetadata())
                        {
                            var reader3 = md3.Reader;
                            CheckNames(new[] { reader0, reader1, reader2, reader3 }, reader3.GetTypeDefNames()); // no additional types
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Local from previous generation is of an anonymous
        /// type not available in next generation.
        /// </summary>
        [Fact]
        public void AnonymousTypes_AddThenDelete()
        {
            var source0 =
@"class C
{
    object A;
    static object F()
    {
        var x = new C();
        var y = x.A;
        return y;
    }
}";
            var source1 =
@"class C
{
    static object F()
    {
        var x = new { A = new object() };
        var y = x.A;
        return y;
    }
}";
            var source2 =
@"class C
{
    static object F()
    {
        var x = new { A = new object(), B = 2 };
        var y = x.A;
        y = new { B = new object() }.B;
        return y;
    }
}";
            var source3 =
@"class C
{
    static object F()
    {
        var x = new { A = new object(), B = 3 };
        var y = x.A;
        return y;
    }
}";
            var source4 =
@"class C
{
    static object F()
    {
        var x = new { B = 4, A = new object() };
        var y = x.A;
        return y;
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation1.WithSource(source2);
            var compilation3 = compilation2.WithSource(source3);
            var compilation4 = compilation3.WithSource(source4);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var generation0 = EmitBaseline.CreateInitialBaseline(md0, testData0.GetMethodData("C.F").EncDebugInfoProvider());

                var method0 = compilation0.GetMember<MethodSymbol>("C.F");
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "C");

                var method1 = compilation1.GetMember<MethodSymbol>("C.F");
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    CheckNames(new[] { reader0, reader1 }, reader1.GetTypeDefNames(), "<>f__AnonymousType0`1"); // one additional type

                    diff1.VerifyIL("C.F", @"
{
  // Code size       27 (0x1b)
  .maxstack  1
  .locals init ([unchanged] V_0,
                object V_1, //y
                [object] V_2,
                <>f__AnonymousType0<object> V_3, //x
                object V_4)
  IL_0000:  nop
  IL_0001:  newobj     ""object..ctor()""
  IL_0006:  newobj     ""<>f__AnonymousType0<object>..ctor(object)""
  IL_000b:  stloc.3
  IL_000c:  ldloc.3
  IL_000d:  callvirt   ""object <>f__AnonymousType0<object>.A.get""
  IL_0012:  stloc.1
  IL_0013:  ldloc.1
  IL_0014:  stloc.s    V_4
  IL_0016:  br.s       IL_0018
  IL_0018:  ldloc.s    V_4
  IL_001a:  ret
}");

                    var method2 = compilation2.GetMember<MethodSymbol>("C.F");
                    // TODO: Generate placeholder for missing types.
#if false
                    var diff2 = compilation2.EmitDifference(
                        diff1.NextGeneration,
                        ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method1, method2, GetLocalMap(method2, method1), preserveLocalVariables: true)));
                    using (var md2 = diff2.GetMetadata())
                    {
                        var reader2 = md2.Reader;
                        CheckNames(new[] { reader0, reader1, reader2 }, reader2.GetTypeDefNames(), "<>f__AnonymousType1`2", "<>f__AnonymousType2`1"); // two additional types

                        diff2.VerifyIL("C.F",
@"{
  // Code size       46 (0x2e)
  .maxstack  2
  .locals init ( V_0,
  object V_1, //y
  V_2,
  V_3,
  V_4,
  <>f__AnonymousType1<object, int> V_5, //x
  object V_6)
  IL_0000:  nop
  IL_0001:  newobj     ""object..ctor()""
  IL_0006:  ldc.i4.2
  IL_0007:  newobj     ""<>f__AnonymousType1<object, int>..ctor(object, int)""
  IL_000c:  stloc.s    V_5
  IL_000e:  ldloc.s    V_5
  IL_0010:  callvirt   ""object <>f__AnonymousType1<object, int>.A.get""
  IL_0015:  stloc.1
  IL_0016:  newobj     ""object..ctor()""
  IL_001b:  newobj     ""<>f__AnonymousType2<object>..ctor(object)""
  IL_0020:  call       ""object <>f__AnonymousType2<object>.B.get""
  IL_0025:  stloc.1
  IL_0026:  ldloc.1
  IL_0027:  stloc.s    V_6
  IL_0029:  br.s       IL_002b
  IL_002b:  ldloc.s    V_6
  IL_002d:  ret
}");

                        var method3 = compilation3.GetMember<MethodSymbol>("C.F");
                        var diff3 = compilation3.EmitDifference(
                            diff2.NextGeneration,
                            ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method2, method3, GetLocalMap(method3, method2), preserveLocalVariables: true)));
                        using (var md3 = diff3.GetMetadata())
                        {
                            var reader3 = md3.Reader;
                            CheckNames(new[] { reader0, reader1, reader2, reader3 }, reader3.GetTypeDefNames()); // no additional types

                            diff3.VerifyIL("C.F",
@"{
  // Code size       30 (0x1e)
  .maxstack  2
  .locals init ( V_0,
  object V_1, //y
  V_2,
  V_3,
  V_4,
  <>f__AnonymousType1<object, int> V_5, //x
  object V_6)
  IL_0000:  nop
  IL_0001:  newobj     ""object..ctor()""
  IL_0006:  ldc.i4.3
  IL_0007:  newobj     ""<>f__AnonymousType1<object, int>..ctor(object, int)""
  IL_000c:  stloc.s    V_5
  IL_000e:  ldloc.s    V_5
  IL_0010:  callvirt   ""object <>f__AnonymousType1<object, int>.A.get""
  IL_0015:  stloc.1
  IL_0016:  ldloc.1
  IL_0017:  stloc.s    V_6
  IL_0019:  br.s       IL_001b
  IL_001b:  ldloc.s    V_6
  IL_001d:  ret
}");

                            var method4 = compilation4.GetMember<MethodSymbol>("C.F");
                            var diff4 = compilation4.EmitDifference(
                                diff3.NextGeneration,
                                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method3, method4, GetLocalMap(method4, method3), preserveLocalVariables: true)));
                            using (var md4 = diff4.GetMetadata())
                            {
                                var reader4 = md4.Reader;
                                CheckNames(new[] { reader0, reader1, reader2, reader3, reader4 }, reader4.GetTypeDefNames(), "<>f__AnonymousType3`2"); // one additional type

                                diff4.VerifyIL("C.F",
@"{
  // Code size       30 (0x1e)
  .maxstack  2
  .locals init ( V_0,
  object V_1, //y
  V_2,
  V_3,
  V_4,
  <>f__AnonymousType3<int, object> V_5, //x
  object V_6)
  IL_0000:  nop
  IL_0001:  ldc.i4.4
  IL_0002:  newobj     ""object..ctor()""
  IL_0007:  newobj     ""<>f__AnonymousType3<int, object>..ctor(int, object)""
  IL_000c:  stloc.s    V_5
  IL_000e:  ldloc.s    V_5
  IL_0010:  callvirt   ""object <>f__AnonymousType3<int, object>.A.get""
  IL_0015:  stloc.1
  IL_0016:  ldloc.1
  IL_0017:  stloc.s    V_6
  IL_0019:  br.s       IL_001b
  IL_001b:  ldloc.s    V_6
  IL_001d:  ret
}");
                            }
                        }
                    }
#endif
                }
            }
        }

        /// <summary>
        /// Should not re-use locals if the method metadata
        /// signature is unsupported.
        /// </summary>
        [Fact(Skip = "TODO")]
        public void LocalType_UnsupportedSignatureContent()
        {
            // Equivalent to C#, but with extra local and required modifier on
            // expected local. Used to generate initial (unsupported) metadata.
            var ilSource =
@".assembly extern mscorlib { .ver 4:0:0:0 .publickeytoken = (B7 7A 5C 56 19 34 E0 89) }
.assembly '<<GeneratedFileName>>' { }
.class C
{
  .method public specialname rtspecialname instance void .ctor()
  {
    ret
  }
  .method private static object F()
  {
    ldnull
    ret
  }
  .method private static void M1()
  {
    .locals init ([0] object other, [1] object modreq(int32) o)
    call object C::F()
    stloc.1
    ldloc.1
    call void C::M2(object)
    ret
  }
  .method private static void M2(object o)
  {
    ret
  }
}";
            var source =
@"class C
{
    static object F()
    {
        return null;
    }
    static void M1()
    {
        object o = F();
        M2(o);
    }
    static void M2(object o)
    {
    }
}";
            ImmutableArray<byte> assemblyBytes;
            ImmutableArray<byte> pdbBytes;
            EmitILToArray(ilSource, appendDefaultHeader: false, includePdb: false, assemblyBytes: out assemblyBytes, pdbBytes: out pdbBytes);
            var md0 = ModuleMetadata.CreateFromImage(assemblyBytes);
            // Still need a compilation with source for the initial
            // generation - to get a MethodSymbol and syntax map.
            var compilation0 = CreateCompilationWithMscorlib(source, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source);

            var method0 = compilation0.GetMember<MethodSymbol>("C.M1");
            var generation0 = EmitBaseline.CreateInitialBaseline(md0, m => default(EditAndContinueMethodDebugInformation));

            var method1 = compilation1.GetMember<MethodSymbol>("C.M1");
            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));

            diff1.VerifyIL("C.M1",
@"{
  // Code size       15 (0xf)
  .maxstack  1
  .locals init (object V_0) //o
  IL_0000:  nop
  IL_0001:  call       ""object C.F()""
  IL_0006:  stloc.0
  IL_0007:  ldloc.0
  IL_0008:  call       ""void C.M2(object)""
  IL_000d:  nop
  IL_000e:  ret
}");
        }
        
        /// <summary>
        /// Should not re-use locals with custom modifiers.
        /// </summary>
        [Fact(Skip = "TODO")]
        public void LocalType_CustomModifiers()
        {
            // Equivalent method signature to C#, but
            // with optional modifier on locals.
            var ilSource =
@".assembly extern mscorlib { .ver 4:0:0:0 .publickeytoken = (B7 7A 5C 56 19 34 E0 89) }
.assembly '<<GeneratedFileName>>' { }
.class public C
{
  .method public specialname rtspecialname instance void .ctor()
  {
    ret
  }
  .method public static object F(class [mscorlib]System.IDisposable d)
  {
    .locals init ([0] class C modopt(int32) c,
                  [1] class [mscorlib]System.IDisposable modopt(object),
                  [2] bool V_2,
                  [3] object V_3)
    ldnull
    ret
  }
}";
            var source =
@"class C
{
    static object F(System.IDisposable d)
    {
        C c;
        using (d)
        {
            c = (C)d;
        }
        return c;
    }
}";
            var metadata0 = (MetadataImageReference)CompileIL(ilSource, appendDefaultHeader: false);
            // Still need a compilation with source for the initial
            // generation - to get a MethodSymbol and syntax map.
            var compilation0 = CreateCompilationWithMscorlib(source, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source);

            var moduleMetadata0 = ((AssemblyMetadata)metadata0.GetMetadata()).GetModules()[0];
            var method0 = compilation0.GetMember<MethodSymbol>("C.F");
            var generation0 = EmitBaseline.CreateInitialBaseline(
                moduleMetadata0,
                m => default(EditAndContinueMethodDebugInformation));

            var method1 = compilation1.GetMember<MethodSymbol>("C.F");
            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));
            
            diff1.VerifyIL("C.F", @"
{
  // Code size       38 (0x26)
  .maxstack  1
  .locals init ([unchanged] V_0,
                [unchanged] V_1,
                [bool] V_2,
                [object] V_3,
                C V_4, //c
                System.IDisposable V_5,
                object V_6)
 -IL_0000:  nop
 -IL_0001:  ldarg.0
  IL_0002:  stloc.s    V_5
  .try
  {
   -IL_0004:  nop
   -IL_0005:  ldarg.0
    IL_0006:  castclass  ""C""
    IL_000b:  stloc.s    V_4
   -IL_000d:  nop
    IL_000e:  leave.s    IL_001d
  }
  finally
  {
   ~IL_0010:  ldloc.s    V_5
    IL_0012:  brfalse.s  IL_001c
    IL_0014:  ldloc.s    V_5
    IL_0016:  callvirt   ""void System.IDisposable.Dispose()""
    IL_001b:  nop
    IL_001c:  endfinally
  }
 -IL_001d:  ldloc.s    V_4
  IL_001f:  stloc.s    V_6
  IL_0021:  br.s       IL_0023
 -IL_0023:  ldloc.s    V_6
  IL_0025:  ret
}", methodToken: diff1.UpdatedMethods.Single());
        }

        /// <summary>
        /// Temporaries for locals used within a single
        /// statement should not be preserved.
        /// </summary>
        [Fact]
        public void TemporaryLocals_Other()
        {
            // Use increment as an example of a compiler generated
            // temporary that does not span multiple statements.
            var source =
@"class C
{
    int P { get; set; }
    static int M()
    {
        var c = new C();
        return c.P++;
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            var methodData0 = testData0.GetMethodData("C.M");
            var method0 = compilation0.GetMember<MethodSymbol>("C.M");
            var generation0 = EmitBaseline.CreateInitialBaseline(
                ModuleMetadata.CreateFromImage(bytes0),
                methodData0.EncDebugInfoProvider());

            var method1 = compilation1.GetMember<MethodSymbol>("C.M");
            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));

            diff1.VerifyIL("C.M", @"
{
  // Code size       37 (0x25)
  .maxstack  3
  .locals init (C V_0, //c
                [unchanged] V_1,
                [int] V_2,
                C V_3,
                int V_4)
  IL_0000:  nop
  IL_0001:  newobj     ""C..ctor()""
  IL_0006:  stloc.0
  IL_0007:  ldloc.0
  IL_0008:  stloc.3
  IL_0009:  ldloc.3
  IL_000a:  callvirt   ""int C.P.get""
  IL_000f:  stloc.s    V_4
  IL_0011:  ldloc.3
  IL_0012:  ldloc.s    V_4
  IL_0014:  ldc.i4.1
  IL_0015:  add
  IL_0016:  callvirt   ""void C.P.set""
  IL_001b:  nop
  IL_001c:  ldloc.s    V_4
  IL_001e:  stloc.s    V_4
  IL_0020:  br.s       IL_0022
  IL_0022:  ldloc.s    V_4
  IL_0024:  ret
}");
        }

        /// Local names array (from PDB) may have fewer slots than method
        /// signature (from metadata) when the trailing slots are unnamed.
        /// </summary>
        [WorkItem(782270, "DevDiv")]
        [Fact]
        public void Bug782270()
        {
            var source =
@"class C
{
    static System.IDisposable F()
    {
        return null;
    }
    static void M()
    {
        using (var o = F())
        {
        }
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            var methodData0 = testData0.GetMethodData("C.M");
            var method0 = compilation0.GetMember<MethodSymbol>("C.M");
            var generation0 = EmitBaseline.CreateInitialBaseline(
                ModuleMetadata.CreateFromImage(bytes0),
                testData0.GetMethodData("C.M").EncDebugInfoProvider());

            testData0.GetMethodData("C.M").VerifyIL(@"
{
  // Code size       23 (0x17)
  .maxstack  1
  .locals init (System.IDisposable V_0) //o
  IL_0000:  nop       
  IL_0001:  call       ""System.IDisposable C.F()""
  IL_0006:  stloc.0   
  .try
  {
    IL_0007:  nop       
    IL_0008:  nop       
    IL_0009:  leave.s    IL_0016
  }
  finally
  {
    IL_000b:  ldloc.0   
    IL_000c:  brfalse.s  IL_0015
    IL_000e:  ldloc.0   
    IL_000f:  callvirt   ""void System.IDisposable.Dispose()""
    IL_0014:  nop       
    IL_0015:  endfinally
  }
  IL_0016:  ret       
}");

            var method1 = compilation1.GetMember<MethodSymbol>("C.M");
            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));

            diff1.VerifyIL("C.M", @"
{
  // Code size       23 (0x17)
  .maxstack  1
  .locals init (System.IDisposable V_0) //o
  IL_0000:  nop       
  IL_0001:  call       ""System.IDisposable C.F()""
  IL_0006:  stloc.0   
  .try
  {
    IL_0007:  nop       
    IL_0008:  nop       
    IL_0009:  leave.s    IL_0016
  }
  finally
  {
    IL_000b:  ldloc.0   
    IL_000c:  brfalse.s  IL_0015
    IL_000e:  ldloc.0   
    IL_000f:  callvirt   ""void System.IDisposable.Dispose()""
    IL_0014:  nop       
    IL_0015:  endfinally
  }
  IL_0016:  ret       
}");
        }

        /// <summary>
        /// Similar to above test but with no named locals in original.
        /// </summary>
        [WorkItem(782270, "DevDiv")]
        [Fact]
        public void Bug782270_NoNamedLocals()
        {
            // Equivalent to C#, but with unnamed locals.
            // Used to generate initial metadata.
            var ilSource =
@".assembly extern mscorlib { .ver 4:0:0:0 .publickeytoken = (B7 7A 5C 56 19 34 E0 89) }
.assembly '<<GeneratedFileName>>' { }
.class C
{
  .method private static class [mscorlib]System.IDisposable F()
  {
    ldnull
    ret
  }
  .method private static void M()
  {
    .locals init ([0] object, [1] object)
    ret
  }
}";
            var source0 =
@"class C
{
    static System.IDisposable F()
    {
        return null;
    }
    static void M()
    {
    }
}";
            var source1 =
@"class C
{
    static System.IDisposable F()
    {
        return null;
    }
    static void M()
    {
        using (var o = F())
        {
        }
    }
}";
            ImmutableArray<byte> assemblyBytes;
            ImmutableArray<byte> pdbBytes;
            EmitILToArray(ilSource, appendDefaultHeader: false, includePdb: false, assemblyBytes: out assemblyBytes, pdbBytes: out pdbBytes);
            var md0 = ModuleMetadata.CreateFromImage(assemblyBytes);
            // Still need a compilation with source for the initial
            // generation - to get a MethodSymbol and syntax map.
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);

            var method0 = compilation0.GetMember<MethodSymbol>("C.M");
            var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);

            var method1 = compilation1.GetMember<MethodSymbol>("C.M");
            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));

            diff1.VerifyIL("C.M", @"
{
  // Code size       23 (0x17)
  .maxstack  1
  .locals init ([object] V_0,
                [object] V_1,
                System.IDisposable V_2) //o
  IL_0000:  nop
  IL_0001:  call       ""System.IDisposable C.F()""
  IL_0006:  stloc.2
  .try
  {
    IL_0007:  nop
    IL_0008:  nop
    IL_0009:  leave.s    IL_0016
  }
  finally
  {
    IL_000b:  ldloc.2
    IL_000c:  brfalse.s  IL_0015
    IL_000e:  ldloc.2
    IL_000f:  callvirt   ""void System.IDisposable.Dispose()""
    IL_0014:  nop
    IL_0015:  endfinally
  }
  IL_0016:  ret
}
");
        }

        [Fact]
        public void TemporaryLocals_ReferencedType()
        {
            var source =
@"class C
{
    static object F()
    {
        return null;
    }
    static void M()
    {
        var x = new System.Collections.Generic.HashSet<int>();
        x.Add(1);
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source, new[] { CSharpRef, SystemCoreRef }, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            var methodData0 = testData0.GetMethodData("C.M");
            var method0 = compilation0.GetMember<MethodSymbol>("C.M");

            var modMeta = ModuleMetadata.CreateFromImage(bytes0);
            var generation0 = EmitBaseline.CreateInitialBaseline(
                modMeta,
                methodData0.EncDebugInfoProvider());

            var method1 = compilation1.GetMember<MethodSymbol>("C.M");
            var diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));

            diff1.VerifyIL("C.M",
@"
{
  // Code size       16 (0x10)
  .maxstack  2
  .locals init (System.Collections.Generic.HashSet<int> V_0) //x
  IL_0000:  nop
  IL_0001:  newobj     ""System.Collections.Generic.HashSet<int>..ctor()""
  IL_0006:  stloc.0
  IL_0007:  ldloc.0
  IL_0008:  ldc.i4.1
  IL_0009:  callvirt   ""bool System.Collections.Generic.HashSet<int>.Add(int)""
  IL_000e:  pop
  IL_000f:  ret
}
");
        }
        /// <summary>
        /// Disallow edits that include "dynamic" operations.
        /// </summary>
        [WorkItem(770502, "DevDiv")]
        [WorkItem(839565, "DevDiv")]
        [Fact]
        public void DynamicOperations()
        {
            var source =
@"class A
{
    static object F = null;
    object x = ((dynamic)F) + 1;
    static A()
    {
        ((dynamic)F).F();
    }
    A() { }
    static void M(object o)
    {
        ((dynamic)o).x = 1;
    }
    static void N(A o)
    {
        o.x = 1;
    }
}
class B
{
    static object F = null;
    static object G = ((dynamic)F).F();
    object x = ((dynamic)F) + 1;
}";
            var compilation0 = CreateCompilationWithMscorlib(source, options: TestOptions.DebugDll, references: new[] { SystemCoreRef, CSharpRef });
            var compilation1 = compilation0.WithSource(source);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                // Source method with dynamic operations.
                var methodData0 = testData0.GetMethodData("A.M");
                var generation0 = EmitBaseline.CreateInitialBaseline(md0, methodData0.EncDebugInfoProvider());
                var method0 = compilation0.GetMember<MethodSymbol>("A.M");
                var method1 = compilation1.GetMember<MethodSymbol>("A.M");
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));
                diff1.EmitResult.Diagnostics.Verify(
                    // (10,17): error CS7097: Cannot continue since the edit includes an operation on a 'dynamic' type.
                    //     static void M(object o)
                    Diagnostic(ErrorCode.ERR_EncNoDynamicOperation, "M").WithLocation(10, 17));

                // Source method with no dynamic operations.
                methodData0 = testData0.GetMethodData("A.N");
                generation0 = EmitBaseline.CreateInitialBaseline(md0, methodData0.EncDebugInfoProvider());
                method0 = compilation0.GetMember<MethodSymbol>("A.N");
                method1 = compilation1.GetMember<MethodSymbol>("A.N");
                diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));
                diff1.EmitResult.Diagnostics.Verify();

                // Explicit .ctor with dynamic operations.
                methodData0 = testData0.GetMethodData("A..ctor");
                generation0 = EmitBaseline.CreateInitialBaseline(md0, methodData0.EncDebugInfoProvider());
                method0 = compilation0.GetMember<MethodSymbol>("A..ctor");
                method1 = compilation1.GetMember<MethodSymbol>("A..ctor");
                diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));
                diff1.EmitResult.Diagnostics.Verify(
                    // (9,5): error CS7097: Cannot continue since the edit includes an operation on a 'dynamic' type.
                    //     A() { }
                    Diagnostic(ErrorCode.ERR_EncNoDynamicOperation, "A").WithLocation(9, 5));

                // Explicit .cctor with dynamic operations.
                methodData0 = testData0.GetMethodData("A..cctor");
                generation0 = EmitBaseline.CreateInitialBaseline(md0, methodData0.EncDebugInfoProvider());
                method0 = compilation0.GetMember<MethodSymbol>("A..cctor");
                method1 = compilation1.GetMember<MethodSymbol>("A..cctor");
                diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));
                diff1.EmitResult.Diagnostics.Verify(
                    // (5,12): error CS7097: Cannot continue since the edit includes an operation on a 'dynamic' type.
                    //     static A()
                    Diagnostic(ErrorCode.ERR_EncNoDynamicOperation, "A").WithLocation(5, 12));

                // Implicit .ctor with dynamic operations.
                methodData0 = testData0.GetMethodData("B..ctor");
                generation0 = EmitBaseline.CreateInitialBaseline(md0, methodData0.EncDebugInfoProvider());
                method0 = compilation0.GetMember<MethodSymbol>("B..ctor");
                method1 = compilation1.GetMember<MethodSymbol>("B..ctor");
                diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));
                diff1.EmitResult.Diagnostics.Verify(
                    // (19,7): error CS7097: Cannot continue since the edit includes an operation on a 'dynamic' type.
                    // class B
                    Diagnostic(ErrorCode.ERR_EncNoDynamicOperation, "B").WithLocation(19, 7));

                // Implicit .cctor with dynamic operations.
                methodData0 = testData0.GetMethodData("B..cctor");
                generation0 = EmitBaseline.CreateInitialBaseline(md0, methodData0.EncDebugInfoProvider());
                method0 = compilation0.GetMember<MethodSymbol>("B..cctor");
                method1 = compilation1.GetMember<MethodSymbol>("B..cctor");
                diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));
                diff1.EmitResult.Diagnostics.Verify(
                    // (19,7): error CS7097: Cannot continue since the edit includes an operation on a 'dynamic' type.
                    // class B
                    Diagnostic(ErrorCode.ERR_EncNoDynamicOperation, "B").WithLocation(19, 7));
            }
        }

        [WorkItem(844472, "DevDiv")]
        [Fact]
        public void MethodSignatureWithNoPIAType()
        {
        var sourcePIA =
@"using System;
using System.Runtime.InteropServices;
[assembly: ImportedFromTypeLib(""_.dll"")]
[assembly: Guid(""35DB1A6B-D635-4320-A062-28D42920E2A3"")]
[ComImport()]
[Guid(""35DB1A6B-D635-4320-A062-28D42920E2A4"")]
public interface I
{
}";
            var source0 =
@"class C
{
    static void M(I x)
    {
        I y = null;
        M(null);
    }
}";
            var source1 =
@"class C
{
    static void M(I x)
    {
        I y = null;
        M(x);
    }
}";
            var compilationPIA = CreateCompilationWithMscorlib(sourcePIA, options: TestOptions.DebugDll);
            var referencePIA = compilationPIA.EmitToImageReference(embedInteropTypes: true);
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll, references: new MetadataReference[] { referencePIA });
            var compilation1 = compilation0.WithSource(source1);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            var methodData0 = testData0.GetMethodData("C.M");
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var generation0 = EmitBaseline.CreateInitialBaseline(md0, methodData0.EncDebugInfoProvider());
                var method0 = compilation0.GetMember<MethodSymbol>("C.M");
                var method1 = compilation1.GetMember<MethodSymbol>("C.M");
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));
                diff1.VerifyIL("C.M",
@"{
  // Code size       11 (0xb)
  .maxstack  1
  .locals init ([unchanged] V_0,
  I V_1) //y
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.1
  IL_0003:  ldarg.0
  IL_0004:  call       ""void C.M(I)""
  IL_0009:  nop
  IL_000a:  ret
}");
            }
        }

        /// <summary>
        /// Disallow edits that require NoPIA references.
        /// </summary>
        [Fact]
        public void NoPIAReferences()
        {
            var sourcePIA =
@"using System;
using System.Runtime.InteropServices;
[assembly: ImportedFromTypeLib(""_.dll"")]
[assembly: Guid(""35DB1A6B-D635-4320-A062-28D42921E2B3"")]
[ComImport()]
[Guid(""35DB1A6B-D635-4320-A062-28D42921E2B4"")]
public interface IA
{
    void M();
    int P { get; }
    event Action E;
}
[ComImport()]
[Guid(""35DB1A6B-D635-4320-A062-28D42921E2B5"")]
public interface IB
{
}
[ComImport()]
[Guid(""35DB1A6B-D635-4320-A062-28D42921E2B6"")]
public interface IC
{
}
public struct S
{
    public object F;
}";
            var source0 =
@"class C<T>
{
    static object F = typeof(IC);
    static void M1()
    {
        var o = default(IA);
        o.M();
        M2(o.P);
        o.E += M1;
        M2(C<IA>.F);
        M2(new S());
    }
    static void M2(object o)
    {
    }
}";
            var source1A = source0;
            var source1B =
@"class C<T>
{
    static object F = typeof(IC);
    static void M1()
    {
        M2(null);
    }
    static void M2(object o)
    {
    }
}";
            var compilationPIA = CreateCompilationWithMscorlib(sourcePIA, options: TestOptions.DebugDll);
            var referencePIA = compilationPIA.EmitToImageReference(embedInteropTypes: true);
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll, references: new MetadataReference[] { referencePIA, SystemCoreRef, CSharpRef });
            var compilation1A = compilation0.WithSource(source1A);
            var compilation1B = compilation0.WithSource(source1B);

            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            var methodData0 = testData0.GetMethodData("C<T>.M1");
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "C`1", "IA", "IC", "S");
                var generation0 = EmitBaseline.CreateInitialBaseline(md0, methodData0.EncDebugInfoProvider());
                var method0 = compilation0.GetMember<MethodSymbol>("C.M1");

                // Disallow edits that require NoPIA references.
                var method1A = compilation1A.GetMember<MethodSymbol>("C.M1");
                var diff1A = compilation1A.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1A, GetEquivalentNodesMap(method1A, method0), preserveLocalVariables: true)));
                diff1A.EmitResult.Diagnostics.Verify(
                    // error CS7094: Cannot continue since the edit includes a reference to an embedded type: 'S'.
                    Diagnostic(ErrorCode.ERR_EncNoPIAReference).WithArguments("S"),
                    // error CS7094: Cannot continue since the edit includes a reference to an embedded type: 'IA'.
                    Diagnostic(ErrorCode.ERR_EncNoPIAReference).WithArguments("IA"));

                // Allow edits that do not require NoPIA references,
                // even if the previous code included references.
                var method1B = compilation1B.GetMember<MethodSymbol>("C.M1");
                var diff1B = compilation1B.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1B, GetEquivalentNodesMap(method1B, method0), preserveLocalVariables: true)));
                diff1B.VerifyIL("C<T>.M1",
@"{
  // Code size        9 (0x9)
  .maxstack  1
  .locals init ([unchanged] V_0,
  [unchanged] V_1)
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  call       ""void C<T>.M2(object)""
  IL_0007:  nop
  IL_0008:  ret
}");
                using (var md1 = diff1B.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    CheckNames(readers, reader1.GetTypeDefNames());
                }
            }
        }

        [WorkItem(844536, "DevDiv")]
        [Fact]
        public void NoPIATypeInNamespace()
        {
            var sourcePIA =
@"using System;
using System.Runtime.InteropServices;
[assembly: ImportedFromTypeLib(""_.dll"")]
[assembly: Guid(""35DB1A6B-D635-4320-A062-28D42920E2A5"")]
namespace N
{
    [ComImport()]
    [Guid(""35DB1A6B-D635-4320-A062-28D42920E2A6"")]
    public interface IA
    {
    }
}
[ComImport()]
[Guid(""35DB1A6B-D635-4320-A062-28D42920E2A7"")]
public interface IB
{
}";
            var source =
@"class C<T>
{
    static void M(object o)
    {
        M(C<N.IA>.E.X);
        M(C<IB>.E.X);
    }
    enum E { X }
}";
            var compilationPIA = CreateCompilationWithMscorlib(sourcePIA, options: TestOptions.DebugDll);
            var referencePIA = compilationPIA.EmitToImageReference(embedInteropTypes: true);
            var compilation0 = CreateCompilationWithMscorlib(source, options: TestOptions.DebugDll, references: new MetadataReference[] { referencePIA, SystemCoreRef, CSharpRef });
            var compilation1 = compilation0.WithSource(source);

            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var generation0 = EmitBaseline.CreateInitialBaseline(md0, m => default(EditAndContinueMethodDebugInformation));
                var method0 = compilation0.GetMember<MethodSymbol>("C.M");
                var method1 = compilation1.GetMember<MethodSymbol>("C.M");
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1)));
                diff1.EmitResult.Diagnostics.Verify(
                    // error CS7094: Cannot continue since the edit includes a reference to an embedded type: 'N.IA'.
                    Diagnostic(ErrorCode.ERR_EncNoPIAReference).WithArguments("N.IA"),
                    // error CS7094: Cannot continue since the edit includes a reference to an embedded type: 'IB'.
                    Diagnostic(ErrorCode.ERR_EncNoPIAReference).WithArguments("IB"));
                diff1.VerifyIL("C<T>.M",
@"{
  // Code size       26 (0x1a)
  .maxstack  1
  IL_0000:  nop
  IL_0001:  ldc.i4.0
  IL_0002:  box        ""C<N.IA>.E""
  IL_0007:  call       ""void C<T>.M(object)""
  IL_000c:  nop
  IL_000d:  ldc.i4.0
  IL_000e:  box        ""C<IB>.E""
  IL_0013:  call       ""void C<T>.M(object)""
  IL_0018:  nop
  IL_0019:  ret
}");
            }
        }

        /// <summary>
        /// Should use TypeDef rather than TypeRef for unrecognized
        /// local of a type defined in the original assembly.
        /// </summary>
        [WorkItem(910777)]
        [Fact]
        public void UnrecognizedLocalOfTypeFromAssembly()
        {
            var source =
@"class E : System.Exception
{
}
class C
{
    static void M()
    {
        try
        {
        }
        catch (E e)
        {
        }
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source);
            var testData0 = new CompilationTestData();
            var bytes0 = compilation0.EmitToArray(testData: testData0);
            var methodData0 = testData0.GetMethodData("C.M");

            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetAssemblyRefNames(), "mscorlib");
                var method0 = compilation0.GetMember<MethodSymbol>("C.M");
                var method1 = compilation1.GetMember<MethodSymbol>("C.M");

                var generation0 = EmitBaseline.CreateInitialBaseline(md0, methodData0.EncDebugInfoProvider());

                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0, method1, GetEquivalentNodesMap(method1, method0), preserveLocalVariables: true)));

                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    CheckNames(readers, reader1.GetAssemblyRefNames(), "mscorlib");
                    CheckNames(readers, reader1.GetTypeRefNames(), "Object");
                    CheckEncLog(reader1,
                        Row(2, TableIndex.AssemblyRef, EditAndContinueOperation.Default),
                        Row(7, TableIndex.TypeRef, EditAndContinueOperation.Default),
                        Row(2, TableIndex.StandAloneSig, EditAndContinueOperation.Default),
                        Row(2, TableIndex.MethodDef, EditAndContinueOperation.Default));
                    CheckEncMap(reader1,
                        Handle(7, TableIndex.TypeRef),
                        Handle(2, TableIndex.MethodDef),
                        Handle(2, TableIndex.StandAloneSig),
                        Handle(2, TableIndex.AssemblyRef));
                }

                diff1.VerifyIL("C.M", @"
{
  // Code size       11 (0xb)
  .maxstack  1
  .locals init (E V_0) //e
  IL_0000:  nop
  .try
  {
    IL_0001:  nop
    IL_0002:  nop
    IL_0003:  leave.s    IL_000a
  }
  catch E
  {
    IL_0005:  stloc.0
    IL_0006:  nop
    IL_0007:  nop
    IL_0008:  leave.s    IL_000a
  }
  IL_000a:  ret
}");
            }
        }

        /// <summary>
        /// Similar to above test but with anonymous type
        /// added in subsequent generation.
        /// </summary>
        [WorkItem(910777)]
        [Fact]
        public void UnrecognizedLocalOfAnonymousTypeFromAssembly()
        {
            var source0 =
@"class C
{
    static string F()
    {
        return null;
    }
    static string G()
    {
        var o = new { Y = 1 };
        return o.ToString();
    }
}";
            var source1 =
@"class C
{
    static string F()
    {
        var o = new { X = 1 };
        return o.ToString();
    }
    static string G()
    {
        var o = new { Y = 1 };
        return o.ToString();
    }
}";
            var source2 =
@"class C
{
    static string F()
    {
        return null;
    }
    static string G()
    {
        return null;
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation1.WithSource(source2);
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetAssemblyRefNames(), "mscorlib");
                var method0F = compilation0.GetMember<MethodSymbol>("C.F");
                // Use empty LocalVariableNameProvider for original locals and
                // use preserveLocalVariables: true for the edit so that existing
                // locals are retained even though all are unrecognized.
                var generation0 = EmitBaseline.CreateInitialBaseline(
                    md0,
                    EmptyLocalsProvider);
                var method1F = compilation1.GetMember<MethodSymbol>("C.F");
                var method1G = compilation1.GetMember<MethodSymbol>("C.G");
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0F, method1F, syntaxMap: s => null, preserveLocalVariables: true)));
                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var readers = new[] { reader0, reader1 };
                    CheckNames(readers, reader1.GetAssemblyRefNames(), "mscorlib");
                    CheckNames(readers, reader1.GetTypeDefNames(), "<>f__AnonymousType1`1");
                    CheckNames(readers, reader1.GetTypeRefNames(), "CompilerGeneratedAttribute", "DebuggerDisplayAttribute", "Object", "DebuggerBrowsableState", "DebuggerBrowsableAttribute", "DebuggerHiddenAttribute", "EqualityComparer`1", "String");
                    // Change method updated in generation 1.
                    var method2F = compilation2.GetMember<MethodSymbol>("C.F");
                    var diff2F = compilation2.EmitDifference(
                        diff1.NextGeneration,
                        ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method1F, method2F, syntaxMap: s => null, preserveLocalVariables: true)));
                    using (var md2 = diff2F.GetMetadata())
                    {
                        var reader2 = md2.Reader;
                        readers = new[] { reader0, reader1, reader2 };
                        CheckNames(readers, reader2.GetAssemblyRefNames(), "mscorlib");
                        CheckNames(readers, reader2.GetTypeDefNames());
                        CheckNames(readers, reader2.GetTypeRefNames(), "Object");
                    }
                    // Change method unchanged since generation 0.
                    var method2G = compilation2.GetMember<MethodSymbol>("C.G");
                    var diff2G = compilation2.EmitDifference(
                        diff1.NextGeneration,
                        ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method1G, method2G, syntaxMap: s => null, preserveLocalVariables: true)));
                    using (var md2 = diff2G.GetMetadata())
                    {
                        var reader2 = md2.Reader;
                        readers = new[] { reader0, reader1, reader2 };
                        CheckNames(readers, reader2.GetAssemblyRefNames(), "mscorlib");
                        CheckNames(readers, reader2.GetTypeDefNames());
                        CheckNames(readers, reader2.GetTypeRefNames(), "Object");
                    }
                }
            }
        }

        [Fact, WorkItem(923492)]
        public void SymWriterErrors()
        {
            var source0 =
@"class C
{
}";
            var source1 =
@"class C
{
    static void Main() { }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);

            // Verify full metadata contains expected rows.
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var diff1 = compilation1.EmitDifference(
                    EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider),
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Insert, null, compilation1.GetMember<MethodSymbol>("C.Main"))),
                    testData: new CompilationTestData { SymWriterFactory = () => new MockSymUnmanagedWriter() });

                diff1.EmitResult.Diagnostics.Verify(
                    // error CS0041: Unexpected error writing debug information -- 'The method or operation is not implemented.'
                    Diagnostic(ErrorCode.FTL_DebugEmitFailure).WithArguments("The method or operation is not implemented."));

                Assert.False(diff1.EmitResult.Success);
            }
        }

        [WorkItem(1058058)]
        [Fact]
        public void BlobContainsInvalidValues()
        {
            var source0 =
@"class C
{
    static void F()
    {
        string foo = ""abc"";
    }
}";
            var source1 =
@"class C
{
    static void F()
    {
        float foo = 10;
    }
}";
            var source2 =
@"class C
{
    static void F()
    {
        bool foo = true;
    }
}";
            var compilation0 = CreateCompilationWithMscorlib(source0, options: TestOptions.DebugDll);
            var compilation1 = compilation0.WithSource(source1);
            var compilation2 = compilation1.WithSource(source2);
            var bytes0 = compilation0.EmitToArray();
            using (var md0 = ModuleMetadata.CreateFromImage(bytes0))
            {
                var reader0 = md0.MetadataReader;
                CheckNames(reader0, reader0.GetAssemblyRefNames(), "mscorlib");
                var method0F = compilation0.GetMember<MethodSymbol>("C.F");
                var generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider);
                var method1F = compilation1.GetMember<MethodSymbol>("C.F");
                var diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method0F, method1F, syntaxMap: s => null, preserveLocalVariables: true)));

                var handle = MetadataTokens.BlobHandle(1);
                byte[] value0 = reader0.GetBlobBytes(handle);
                Assert.Equal("20-01-01-08", BitConverter.ToString(value0));

                using (var md1 = diff1.GetMetadata())
                {
                    var reader1 = md1.Reader;
                    var method2F = compilation2.GetMember<MethodSymbol>("C.F");
                    var diff2F = compilation2.EmitDifference(
                        diff1.NextGeneration,
                        ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, method1F, method2F, syntaxMap: s => null, preserveLocalVariables: true)));

                    byte[] value1 = reader1.GetBlobBytes(handle);
                    Assert.Equal("07-02-0E-0C", BitConverter.ToString(value1));

                    using (var md2 = diff2F.GetMetadata())
                    {
                        var reader2 = md2.Reader;
                        byte[] value2 = reader2.GetBlobBytes(handle);
                        Assert.Equal("07-03-0E-0C-02", BitConverter.ToString(value2));
                    }
                }
            }
        }

        [Fact]
        public void ReferenceToMemberAddedToAnotherAssembly1()
        {
            var sourceA0 = @"
public class A
{
}
";
            var sourceA1 = @"
public class A
{
    public void M() { System.Console.WriteLine(1);}
}

public class X {} 
";
            var sourceB0 = @"
public class B
{
    public static void F() { }
}";
            var sourceB1 = @"
public class B
{
    public static void F() { new A().M(); }
}

public class Y : X { }
";

            var compilationA0 = CreateCompilationWithMscorlib(sourceA0, options: TestOptions.DebugDll, assemblyName: "LibA");
            var compilationA1 = compilationA0.WithSource(sourceA1);
            var compilationB0 = CreateCompilationWithMscorlib(sourceB0, new[] { compilationA0.ToMetadataReference() }, options: TestOptions.DebugDll, assemblyName: "LibB");
            var compilationB1 = CreateCompilationWithMscorlib(sourceB1, new[] { compilationA1.ToMetadataReference() }, options: TestOptions.DebugDll, assemblyName: "LibB");

            var bytesA0 = compilationA0.EmitToArray();
            var bytesB0 = compilationB0.EmitToArray();
            var mdA0 = ModuleMetadata.CreateFromImage(bytesA0);
            var mdB0 = ModuleMetadata.CreateFromImage(bytesB0);
            var generationA0 = EmitBaseline.CreateInitialBaseline(mdA0, EmptyLocalsProvider);
            var generationB0 = EmitBaseline.CreateInitialBaseline(mdB0, EmptyLocalsProvider);
            var mA1 = compilationA1.GetMember<MethodSymbol>("A.M");
            var mX1 = compilationA1.GetMember<TypeSymbol>("X");

            var allAddedSymbols = new ISymbol[] { mA1, mX1 };

            var diffA1 = compilationA1.EmitDifference(
                generationA0,
                ImmutableArray.Create(
                    new SemanticEdit(SemanticEditKind.Insert, null, mA1),
                    new SemanticEdit(SemanticEditKind.Insert, null, mX1)),
                allAddedSymbols);

            diffA1.EmitResult.Diagnostics.Verify();

            var diffB1 = compilationB1.EmitDifference(
                generationB0,
                ImmutableArray.Create(
                    new SemanticEdit(SemanticEditKind.Update, compilationB0.GetMember<MethodSymbol>("B.F"), compilationB1.GetMember<MethodSymbol>("B.F")),
                    new SemanticEdit(SemanticEditKind.Insert, null, compilationB1.GetMember<TypeSymbol>("Y"))),
                allAddedSymbols);

            diffB1.EmitResult.Diagnostics.Verify(
                // (7,14): error CS7101: Member 'X' added during the current debug session can only be accessed from within its declaring assembly 'LibA'.
                // public class X {} 
                Diagnostic(ErrorCode.ERR_EncReferenceToAddedMember, "X").WithArguments("X", "LibA").WithLocation(7, 14),
                // (4,17): error CS7101: Member 'M' added during the current debug session can only be accessed from within its declaring assembly 'LibA'.
                //     public void M() { System.Console.WriteLine(1);}
                Diagnostic(ErrorCode.ERR_EncReferenceToAddedMember, "M").WithArguments("M", "LibA").WithLocation(4, 17));
        }

        [Fact]
        public void ReferenceToMemberAddedToAnotherAssembly2()
        {
            var sourceA = @"
public class A
{
    public void M() { }
}";
            var sourceB0 = @"
public class B
{
    public static void F() { var a = new A(); }
}";
            var sourceB1 = @"
public class B
{
    public static void F() { var a = new A(); a.M(); }
}";
            var sourceB2 = @"
public class B
{
    public static void F() { var a = new A(); }
}";

            var compilationA = CreateCompilationWithMscorlib(sourceA, options: TestOptions.DebugDll, assemblyName: "AssemblyA");
            var aRef = compilationA.ToMetadataReference();

            var compilationB0 = CreateCompilationWithMscorlib(sourceB0, new[] { aRef }, options: TestOptions.DebugDll, assemblyName: "AssemblyB");
            var compilationB1 = compilationB0.WithSource(sourceB1);
            var compilationB2 = compilationB1.WithSource(sourceB2);

            var testDataB0 = new CompilationTestData();
            var bytesB0 = compilationB0.EmitToArray(testData: testDataB0);
            var mdB0 = ModuleMetadata.CreateFromImage(bytesB0);
            var generationB0 = EmitBaseline.CreateInitialBaseline(mdB0, testDataB0.GetMethodData("B.F").EncDebugInfoProvider());

            var f0 = compilationB0.GetMember<MethodSymbol>("B.F");
            var f1 = compilationB1.GetMember<MethodSymbol>("B.F");
            var f2 = compilationB2.GetMember<MethodSymbol>("B.F");

            var diffB1 = compilationB1.EmitDifference(
                generationB0,
                ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, f0, f1, GetEquivalentNodesMap(f1, f0), preserveLocalVariables: true)));

            diffB1.VerifyIL("B.F", @"
{
  // Code size       15 (0xf)
  .maxstack  1
  .locals init (A V_0) //a
  IL_0000:  nop
  IL_0001:  newobj     ""A..ctor()""
  IL_0006:  stloc.0
  IL_0007:  ldloc.0
  IL_0008:  callvirt   ""void A.M()""
  IL_000d:  nop
  IL_000e:  ret
}
");

            var diffB2 = compilationB2.EmitDifference(
               diffB1.NextGeneration,
               ImmutableArray.Create(new SemanticEdit(SemanticEditKind.Update, f1, f2, GetEquivalentNodesMap(f2, f1), preserveLocalVariables: true)));

            diffB2.VerifyIL("B.F", @"
{
  // Code size        8 (0x8)
  .maxstack  1
  .locals init (A V_0) //a
  IL_0000:  nop
  IL_0001:  newobj     ""A..ctor()""
  IL_0006:  stloc.0
  IL_0007:  ret
}
");
        }
    }
}
