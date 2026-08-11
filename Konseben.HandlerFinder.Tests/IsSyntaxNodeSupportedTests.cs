using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Konseben.HandlerFinder.Tests
{
    public class IsSyntaxNodeSupportedTests
    {
        [Fact]
        public void Null_IsNotSupported()
        {
            Assert.False(HandlerSearch.IsSyntaxNodeSupported(null));
        }

        [Fact]
        public void ClassDeclaration_IsSupported()
        {
            var node = RoslynTestHelper.FirstNode<ClassDeclarationSyntax>("class GetFooQuery { }");

            Assert.True(HandlerSearch.IsSyntaxNodeSupported(node));
        }

        [Fact]
        public void RecordDeclaration_IsSupported()
        {
            var node = RoslynTestHelper.FirstNode<RecordDeclarationSyntax>("record GetFooQuery(int Id);");

            Assert.True(HandlerSearch.IsSyntaxNodeSupported(node));
        }

        [Fact]
        public void ConstructorDeclaration_IsSupported()
        {
            var node = RoslynTestHelper.FirstNode<ConstructorDeclarationSyntax>(
                "class GetFooQuery { public GetFooQuery() { } }");

            Assert.True(HandlerSearch.IsSyntaxNodeSupported(node));
        }

        [Fact]
        public void IdentifierName_IsSupported()
        {
            var node = RoslynTestHelper.FirstNode<IdentifierNameSyntax>("class C { GetFooQuery M() => null; }");

            Assert.True(HandlerSearch.IsSyntaxNodeSupported(node));
        }

        [Fact]
        public void VarIdentifier_IsNotSupported()
        {
            var node = RoslynTestHelper.FirstNode<IdentifierNameSyntax>("class C { void M() { var x = 1; } }");

            Assert.False(HandlerSearch.IsSyntaxNodeSupported(node));
        }

        [Fact]
        public void MethodDeclaration_IsNotSupported()
        {
            // A method declaration is not one of the supported invocation targets.
            var node = RoslynTestHelper.FirstNode<MethodDeclarationSyntax>("class C { void Handle() { } }");

            Assert.False(HandlerSearch.IsSyntaxNodeSupported(node));
        }
    }
}
