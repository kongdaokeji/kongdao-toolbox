using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Data;
using Windows.Foundation;

namespace TubaWinUi3.Pages;

/// <summary>由 ListView 原生增量加载机制请求下一批，始终向现有集合追加。</summary>
public sealed partial class FeatureIncrementalCollection : ObservableCollection<FeatureRowVm>, ISupportIncrementalLoading
{
    private const int BatchSize = 100;
    private readonly IReadOnlyList<FeatureRowVm> _source;
    private readonly DispatcherQueue _dispatcher;
    public event Action? BatchLoaded;
    public bool HasMoreItems => Count < _source.Count;

    public FeatureIncrementalCollection(IReadOnlyList<FeatureRowVm> source, DispatcherQueue dispatcher)
    {
        _source = source;
        _dispatcher = dispatcher;
        AppendBatch();
    }

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
    {
        // 数据已在内存中。原生 ListView 在 UI 线程请求，直接追加并返回已完成操作，
        // 避免集合通知与异步完成回调分处不同线程，使后续请求停留在加载状态。
        if (!_dispatcher.HasThreadAccess)
            throw new InvalidOperationException("功能列表必须在 UI 线程加载。");
        var added = AppendBatch();
        BatchLoaded?.Invoke();
        return Task.FromResult(new LoadMoreItemsResult { Count = (uint)added }).AsAsyncOperation();
    }

    private int AppendBatch()
    {
        var before = Count;
        var end = Math.Min(before + BatchSize, _source.Count);
        for (var i = before; i < end; i++) Add(_source[i]);
        return Count - before;
    }
}
