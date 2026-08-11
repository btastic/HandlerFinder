using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Konseben.HandlerFinder.Tests
{
    public class FindHandlersAsyncTests
    {
        private const string ClassHandler =
@"using System.Threading.Tasks;

public class GetFooQueryHandler
{
    public Task Handle(GetFooQuery request)
    {
        return Task.CompletedTask;
    }
}";

        [Fact]
        public async Task Matches_ClassHandler_ByParameterTypeName()
        {
            Solution solution = RoslynTestHelper.CreateSolution(("GetFooQueryHandler.cs", ClassHandler));

            var results = await HandlerSearch.FindHandlersAsync(solution, "GetFooQuery");

            var result = Assert.Single(results);
            Assert.Equal("GetFooQueryHandler.cs", result.file);
            Assert.Equal(5, result.lineIndex);
        }

        [Fact]
        public async Task Matches_QualifiedNameParameterType()
        {
            const string source =
@"using System.Threading.Tasks;

public class GetFooQueryHandler
{
    public Task Handle(My.Namespace.GetFooQuery request)
    {
        return Task.CompletedTask;
    }
}";
            Solution solution = RoslynTestHelper.CreateSolution(("GetFooQueryHandler.cs", source));

            var results = await HandlerSearch.FindHandlersAsync(solution, "GetFooQuery");

            var result = Assert.Single(results);
            Assert.Equal(5, result.lineIndex);
        }

        [Fact]
        public async Task DoesNotMatch_WhenNameDiffers()
        {
            Solution solution = RoslynTestHelper.CreateSolution(("GetFooQueryHandler.cs", ClassHandler));

            var results = await HandlerSearch.FindHandlersAsync(solution, "GetBarQuery");

            Assert.Empty(results);
        }

        [Fact]
        public async Task DoesNotMatch_WhenMethodIsNotNamedHandle()
        {
            const string source =
@"public class GetFooQueryHandler
{
    public void Process(GetFooQuery request) { }
}";
            Solution solution = RoslynTestHelper.CreateSolution(("GetFooQueryHandler.cs", source));

            var results = await HandlerSearch.FindHandlersAsync(solution, "GetFooQuery");

            Assert.Empty(results);
        }

        [Fact]
        public async Task Matches_MultipleHandlersAcrossDocuments()
        {
            const string second =
@"public class OtherHandler
{
    public void Handle(GetFooQuery request) { }
}";
            Solution solution = RoslynTestHelper.CreateSolution(
                ("GetFooQueryHandler.cs", ClassHandler),
                ("OtherHandler.cs", second));

            var results = await HandlerSearch.FindHandlersAsync(solution, "GetFooQuery");

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.file == "GetFooQueryHandler.cs");
            Assert.Contains(results, r => r.file == "OtherHandler.cs");
        }

        [Fact]
        public async Task Ignores_HandleMethodWithoutMatchingParameter()
        {
            const string source =
@"public class SomeHandler
{
    public void Handle(SomethingElse request) { }
}";
            Solution solution = RoslynTestHelper.CreateSolution(("SomeHandler.cs", source));

            var results = await HandlerSearch.FindHandlersAsync(solution, "GetFooQuery");

            Assert.Empty(results);
        }

        [Fact]
        public async Task ReturnsEmpty_ForNullOrWhitespaceRequest()
        {
            Solution solution = RoslynTestHelper.CreateSolution(("GetFooQueryHandler.cs", ClassHandler));

            Assert.Empty(await HandlerSearch.FindHandlersAsync(solution, null));
            Assert.Empty(await HandlerSearch.FindHandlersAsync(solution, "   "));
        }

        [Fact]
        public async Task ColumnIndex_PointsAtHandleOnSingleLineMethod()
        {
            // Single-line handler avoids newline-normalization ambiguity, pinning the
            // current column behavior exactly. Class name deliberately contains no
            // "Handle" substring so the reported column is the method name's position
            // within the method's own text (1-based).
            const string source =
                "public class FooProcessor { public System.Threading.Tasks.Task Handle(GetFooQuery request) { return null; } }";

            Solution solution = RoslynTestHelper.CreateSolution(("FooProcessor.cs", source));

            var results = await HandlerSearch.FindHandlersAsync(solution, "GetFooQuery");

            var result = Assert.Single(results);
            Assert.Equal(1, result.lineIndex);
            Assert.Equal(36, result.columnIndex);
        }
    }
}
