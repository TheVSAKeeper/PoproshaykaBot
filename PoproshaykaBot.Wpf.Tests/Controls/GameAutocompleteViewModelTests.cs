using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Wpf.ViewModels.Controls;
using System.Collections.Concurrent;

namespace PoproshaykaBot.Wpf.Tests.Controls;

[TestFixture]
public sealed class GameAutocompleteViewModelTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task SecondSearch_WhileFirstPending_CancelsFirstAndShowsOnlySecond(bool firstHonorsCancellation)
    {
        var stale = new TaskCompletionSource<IReadOnlyList<GameSuggestion>>();
        var fresh = new GameSuggestion("2", "Half-Life 2", string.Empty);

        var resolver = new ScriptedResolver((query, token) =>
        {
            if (query != "ha")
            {
                return Task.FromResult<IReadOnlyList<GameSuggestion>>([fresh]);
            }

            if (firstHonorsCancellation)
            {
                token.Register(() => stale.TrySetCanceled(token));
            }

            return stale.Task;
        });

        var dispatcher = new ManualUiDispatcher();
        var viewModel = new GameAutocompleteViewModel(resolver, dispatcher);
        var debounce = dispatcher.Timers.Single();

        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GameAutocompleteViewModel.IsSuggestionsOpen) && viewModel.IsSuggestionsOpen)
            {
                opened.TrySetResult();
            }
        };

        viewModel.Query = "ha";
        debounce.Tick();
        viewModel.Query = "hal";
        debounce.Tick();

        await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stale.TrySetResult([new("1", "Halo", string.Empty)]);

        var calls = resolver.Calls.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(calls.Select(call => call.Query), Is.EqualTo(new[] { "ha", "hal" }));
            Assert.That(calls[0].Token.IsCancellationRequested, Is.True, "Второй поиск отменяет первый");
            Assert.That(calls[1].Token.IsCancellationRequested, Is.False);
            Assert.That(viewModel.Suggestions, Is.EqualTo(new[] { fresh }), "Поздний ответ отменённого поиска не перетирает список");
        });

        viewModel.Dispose();

        Assert.That(calls[1].Token.IsCancellationRequested, Is.True, "Освобождение отменяет идущий поиск");
    }

    private sealed class ScriptedResolver(Func<string, CancellationToken, Task<IReadOnlyList<GameSuggestion>>> search) : IGameCategoryResolver
    {
        public ConcurrentQueue<(string Query, CancellationToken Token)> Calls { get; } = new();

        public Task<IReadOnlyList<GameSuggestion>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            Calls.Enqueue((query, cancellationToken));
            return search(query, cancellationToken);
        }

        public Task<GameCategoryResolution> ResolveAsync(string query, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task RememberAsync(GameSuggestion suggestion)
        {
            return Task.CompletedTask;
        }
    }
}
