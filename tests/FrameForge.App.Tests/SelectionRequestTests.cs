using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Data;
using FrameForge.App.Controls;

namespace FrameForge.App.Tests;

/// <summary>Real WPF list box, bound like the Library view, on an STA thread.</summary>
public sealed class SelectionRequestTests
{
    private sealed class Sink : ISelectedItemsSink, INotifyPropertyChanged
    {
        private object? _selected;

        public event EventHandler<IReadOnlyList<object>>? SelectionRequested;

        public event PropertyChangedEventHandler? PropertyChanged;

        public IReadOnlyList<object> LastSelection { get; private set; } = [];

        /// <summary>Bound two-way to SelectedItem, like LibraryViewModel.SelectedPhoto.</summary>
        public object? Selected
        {
            get => _selected;
            set
            {
                if (!Equals(_selected, value))
                {
                    _selected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
                }
            }
        }

        public void UpdateSelection(IReadOnlyList<object> items) => LastSelection = items;

        public void Request(IReadOnlyList<object> items) => SelectionRequested?.Invoke(this, items);
    }

    private static void RunSta(Action action, [CallerMemberName] string name = "")
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        { Name = name };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("STA test failed", failure);
        }
    }

    [Fact]
    public void RequestedSelection_SelectsEveryItem_WhenPrimaryItemChanges() => RunSta(() =>
    {
        var items = Enumerable.Range(0, 8).Select(i => $"item{i}").ToList();
        var sink = new Sink();
        var list = new PhotoGridListBox { SelectionMode = SelectionMode.Extended, ItemsSource = items, DataContext = sink };
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(Sink.Selected)) { Mode = BindingMode.TwoWay });
        ListBoxBehaviors.SetSelectedItemsSink(list, sink);

        sink.Selected = items[5]; // previously focused photo, not in the requested group's first position

        sink.Request([items[2], items[3], items[4], items[5]]);

        Assert.Equal(["item2", "item3", "item4", "item5"], list.SelectedItems.Cast<string>().OrderBy(s => s, StringComparer.Ordinal));
        Assert.Equal(4, sink.LastSelection.Count);
        Assert.Equal("item2", sink.Selected);
    });

    [Fact]
    public void RequestedSelection_SurvivesFocusingTheFirstItem_InAShownWindow() => RunSta(() =>
    {
        var items = Enumerable.Range(0, 8).Select(i => $"item{i}").ToList();
        var sink = new Sink();
        var list = new PhotoGridListBox { SelectionMode = SelectionMode.Extended, ItemsSource = items, DataContext = sink };
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(Sink.Selected)) { Mode = BindingMode.TwoWay });
        ListBoxBehaviors.SetSelectedItemsSink(list, sink);
        ListBoxBehaviors.SetScrollSelectionIntoView(list, true);
        var window = new System.Windows.Window { Content = list, Width = 300, Height = 400, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            sink.Selected = items[5];
            ((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(5)).Focus();

            sink.Request([items[2], items[3], items[4], items[5]]);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            Assert.Equal(4, list.SelectedItems.Count);
            Assert.Equal(4, sink.LastSelection.Count);
        }
        finally
        {
            window.Close();
        }
    });
}
