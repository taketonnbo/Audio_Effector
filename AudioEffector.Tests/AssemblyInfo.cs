using Xunit;

// WPF（System.Windows.Application / Dispatcher / STAスレッド）の並列実行競合によるデッドロックおよびハングを防止するため、
// テストコレクションの並列実行を無効化します。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
