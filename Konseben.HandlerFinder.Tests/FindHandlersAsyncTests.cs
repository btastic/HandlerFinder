using System.Linq;
using System.Threading;
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
        public async Task ColumnIndex_PointsAtHandleIdentifierOnSingleLineMethod()
        {
            const string source =
                "public class FooProcessor { public System.Threading.Tasks.Task Handle(GetFooQuery request) { return null; } }";

            Solution solution = RoslynTestHelper.CreateSolution(("FooProcessor.cs", source));

            var results = await HandlerSearch.FindHandlersAsync(solution, "GetFooQuery");

            var result = Assert.Single(results);
            Assert.Equal(1, result.lineIndex);
            Assert.Equal(source.IndexOf("Handle", System.StringComparison.Ordinal) + 1, result.columnIndex);
        }

        [Fact]
        public async Task Matches_WhenRequestNameStraddlesChunkBoundary()
        {
            // The text pre-filter scans in 16384-char chunks with overlap. Position the
            // sole occurrence of the request name so it spans the first chunk boundary,
            // to prove a match straddling two chunks is still found.
            const string handler =
@"public class BigHandler
{
    public void Handle(GetFooQuery request) { }
}";
            int indexInHandler = handler.IndexOf("GetFooQuery", System.StringComparison.Ordinal);
            int targetStart = 16384 - 5; // 5 chars in the first chunk, the rest in the next
            int padLength = targetStart - indexInHandler;

            // A single line comment of exactly padLength chars ("//" + fill + "\n"),
            // containing neither needle.
            string padding = "//" + new string('x', padLength - 3) + "\n";
            string source = padding + handler;

            Assert.Equal(targetStart, source.IndexOf("GetFooQuery", System.StringComparison.Ordinal));

            Solution solution = RoslynTestHelper.CreateSolution(("BigHandler.cs", source));

            var results = await HandlerSearch.FindHandlersAsync(solution, "GetFooQuery");

            Assert.Single(results);
        }

        [Fact]
        public async Task Throws_WhenCancellationAlreadyRequested()
        {
            Solution solution = RoslynTestHelper.CreateSolution(("GetFooQueryHandler.cs", ClassHandler));

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<System.OperationCanceledException>(
                () => HandlerSearch.FindHandlersAsync(solution, "GetFooQuery", cts.Token));
        }

        [Fact]
        public async Task LineAndColumn_PointAtHandleIdentifier_OnMultiLineMethod()
        {
            Solution solution = RoslynTestHelper.CreateSolution(("GetFooQueryHandler.cs", ClassHandler));

            var results = await HandlerSearch.FindHandlersAsync(solution, "GetFooQuery");

            var result = Assert.Single(results);
            Assert.Equal(5, result.lineIndex);
            Assert.Equal(17, result.columnIndex);
        }
    }
}
