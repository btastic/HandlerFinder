using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Konseben.HandlerFinder.Tests
{
    public class GetIdentifierNameByNodeTests
    {
        [Fact]
        public void ClassDeclaration_ReturnsClassName()
        {
            var node = RoslynTestHelper.FirstNode<ClassDeclarationSyntax>("class GetFooQuery { }");

            Assert.Equal("GetFooQuery", HandlerSearch.GetIdentifierNameByNode(node));
        }

        [Fact]
        public void RecordDeclaration_ReturnsRecordName()
        {
            var node = RoslynTestHelper.FirstNode<RecordDeclarationSyntax>("record GetFooQuery(int Id);");

            Assert.Equal("GetFooQuery", HandlerSearch.GetIdentifierNameByNode(node));
        }

        [Fact]
        public void ConstructorDeclaration_ReturnsTypeName()
        {
            var node = RoslynTestHelper.FirstNode<ConstructorDeclarationSyntax>(
                "class GetFooQuery { public GetFooQuery() { } }");

            Assert.Equal("GetFooQuery", HandlerSearch.GetIdentifierNameByNode(node));
        }

        [Fact]
        public void IdentifierName_ReturnsIdentifier()
        {
            // The identifier used as the return type of the method.
            var node = RoslynTestHelper.FirstNode<IdentifierNameSyntax>("class C { GetFooQuery M() => null; }");

            Assert.Equal("GetFooQuery", HandlerSearch.GetIdentifierNameByNode(node));
        }

        [Fact]
        public void SimpleParameterType_ReturnsTypeName()
        {
            var node = RoslynTestHelper.FirstNode<ParameterSyntax>("class C { void M(GetFooQuery request) { } }");

            Assert.Equal("GetFooQuery", HandlerSearch.GetIdentifierNameByNode(node));
        }

        [Fact]
        public void QualifiedNameParameterType_ReturnsRightmostName()
        {
            var node = RoslynTestHelper.FirstNode<ParameterSyntax>(
                "class C { void M(My.Namespace.GetFooQuery request) { } }");

            Assert.Equal("GetFooQuery", HandlerSearch.GetIdentifierNameByNode(node));
        }

        [Fact]
        public void VarIdentifier_ReturnsEmpty()
        {
            var node = RoslynTestHelper.FirstNode<IdentifierNameSyntax>("class C { void M() { var x = 1; } }");

            Assert.Equal(string.Empty, HandlerSearch.GetIdentifierNameByNode(node));
        }
    }
}
