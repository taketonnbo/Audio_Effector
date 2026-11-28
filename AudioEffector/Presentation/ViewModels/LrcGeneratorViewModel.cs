using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;
using Microsoft.Win32;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 右側タブ下部拡張領域の LRC 自動生成パネル（<c>LrcGeneratorView</c>）の状態と操作を統括する ViewModel です。
/// テキストファイル（.txt）からの強制アライメント生成と、音声からの直接文字起こし生成の 2 モードを独立して提供します。
/// </summary>
public sealed class LrcGeneratorViewModel : ViewModelBase, IDisposable
{
    private const double PROGRESS_REPORT_MIN_DELTA = 1.0;

    private readonly LrcGenerationApplicationService _generationService;
    private readonly IAudioService _audioService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Func<string?> _selectTextFileFunc;
    private readonly Func<string?> _selectModelFileFunc;
    private readonly Action<string> _openFolderAction;

    private readonly GeneratorCommand _downloadDefaultModelCommand;
    private readonly GeneratorCommand _selectCustomModelFileCommand;
    private readonly GeneratorCommand _cancelModelDownloadCommand;
    private readonly GeneratorCommand _selectTextFileCommand;
    private readonly GeneratorCommand _clearTextFileCommand;
    private readonly GeneratorCommand _confirmEncodingConversionCommand;
    private readonly GeneratorCommand _cancelEncodingConversionCommand;
    private readonly GeneratorCommand _generateLrcCommand;
    private readonly GeneratorCommand _transcribeAudioCommand;
    private readonly GeneratorCommand _cancelGenerationCommand;
    private readonly GeneratorCommand _adjustOffsetCommand;
    private readonly GeneratorCommand _resetOffsetCommand;
    private readonly GeneratorCommand _regenerateCommand;
    private readonly GeneratorCommand _seekToPreviewLineCommand;
    private readonly GeneratorCommand _saveLrcCommand;
    private readonly GeneratorCommand _confirmOverwriteSaveCommand;
    private readonly GeneratorCommand _cancelOverwriteSaveCommand;
    private readonly GeneratorCommand _openSavedFolderCommand;
    private readonly GeneratorCommand _backToLyricsCommand;
    private readonly GeneratorCommand _closeGeneratorCommand;
    private readonly GeneratorCommand _retryErrorCommand;
    private readonly GeneratorCommand _returnFromErrorCommand;

    private readonly object _ctsLock = new();
    private CancellationTokenSource? _operationCts;
    private bool _disposed;

    private Track? _targetTrack;
    private string? _inputTextFilePath;
    private int _inputTextLineCount;
    private bool _isEncodingConversionApproved;
    private bool _isEncodingWarningVisible;
    private string _detectedEncodingName = string.Empty;
    private string _encodingWarningMessage = string.Empty;
    private string _validationErrorMessage = string.Empty;

    private LocalSpeechModelInfo _activeModelInfo;
    private bool _isModelDownloading;
    private double _modelDownloadProgress;
    private string _modelDownloadStatusMessage = string.Empty;

    private LrcLanguageOption _selectedLanguageOption;
    private bool _isGenerating;
    private bool _isCancelling;
    private double _progress;
    private string _statusMessage = string.Empty;
    private string _currentPhaseLabel = string.Empty;
    private AlignmentPhase _lastReportedPhase = AlignmentPhase.DecodingAudio;
    private double _lastReportedPercent = -10.0;

    private LyricsDocument? _generatedDocument;
    private string _previewLrcText = string.Empty;
    private double _totalOffsetSeconds;
    private string? _savedFilePath;
    private string _saveCompletedMessage = string.Empty;
    private bool _isOverwriteConfirmVisible;
    private string _overwriteWarningMessage = string.Empty;

    private bool _hasError;
    private string _errorTitle = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isModelDownloadError;
    private LrcGenerationMode _lastExecutedMode = LrcGenerationMode.TextAlignment;

    /// <summary>
    /// 同期歌詞表示パネル（<c>LyricsView</c>）へ戻る操作が要求された際に発生するイベント
    /// </summary>
    public event Action? BackToLyricsRequested;

    /// <summary>
    /// LRC 自動生成パネルを閉じる操作が要求された際に発生するイベント
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// `.lrc` ファイルの保存が完了した際に発生するイベント（保存先パスを通知）
    /// </summary>
    public event Action<string>? LrcSaved;

    /// <summary>
    /// 音声認識言語の選択肢一覧を取得します
    /// </summary>
    public IReadOnlyList<LrcLanguageOption> LanguageOptions { get; } =
    [
        new("auto", "自動判定"),
        new("ja", "日本語 (ja)"),
        new("en", "英語 (en)"),
    ];

    /// <summary>
    /// 生成結果 LRC のプレビュー行コレクションを取得します
    /// </summary>
    public ObservableCollection<LrcPreviewLineViewModel> PreviewLines { get; } = new();

    /// <summary>
    /// 生成対象の楽曲を取得します
    /// </summary>
    public Track? TargetTrack
    {
        get => _targetTrack;
        private set
        {
            if (SetProperty(ref _targetTrack, value))
            {
                OnPropertyChanged(nameof(TargetTrackDisplay));
                OnPropertyChanged(nameof(DestinationFilePath));
                InvalidateAllCommands();
            }
        }
    }

    /// <summary>
    /// ヘッダー表示用の対象楽曲名文字列を取得します
    /// </summary>
    public string TargetTrackDisplay => _targetTrack is null
        ? "楽曲未選択"
        : string.IsNullOrWhiteSpace(_targetTrack.Artist)
            ? _targetTrack.Title
            : $"{_targetTrack.Title} - {_targetTrack.Artist}";

    /// <summary>
    /// 選択中の入力テキストファイルパスを取得します
    /// </summary>
    public string? InputTextFilePath
    {
        get => _inputTextFilePath;
        private set
        {
            if (SetProperty(ref _inputTextFilePath, value))
            {
                OnPropertyChanged(nameof(HasInputTextFile));
                OnPropertyChanged(nameof(InputTextFileName));
                OnPropertyChanged(nameof(InputTextSummary));
                InvalidateAllCommands();
            }
        }
    }

    /// <summary>
    /// 入力テキストファイルが選択されているかどうかを取得します
    /// </summary>
    public bool HasInputTextFile => !string.IsNullOrWhiteSpace(_inputTextFilePath);

    /// <summary>
    /// 入力テキストファイルのファイル名を取得します
    /// </summary>
    public string InputTextFileName => string.IsNullOrWhiteSpace(_inputTextFilePath)
        ? string.Empty
        : Path.GetFileName(_inputTextFilePath);

    /// <summary>
    /// 入力テキストファイルの文字コードおよび行数サマリ文字列を取得します
    /// </summary>
    public string InputTextSummary
    {
        get
        {
            if (!HasInputTextFile)
            {
                return string.Empty;
            }

            string enc = string.IsNullOrEmpty(_detectedEncodingName) ? "UTF-8" : _detectedEncodingName;
            string status = _isEncodingWarningVisible ? "承諾待ち" : $"{_inputTextLineCount} 行";
            return $"{enc} / {status}";
        }
    }

    /// <summary>
    /// 非 UTF-8 警告・変換承諾インラインバーを表示すべきかどうかを取得します
    /// </summary>
    public bool IsEncodingWarningVisible
    {
        get => _isEncodingWarningVisible;
        private set
        {
            if (SetProperty(ref _isEncodingWarningVisible, value))
            {
                OnPropertyChanged(nameof(InputTextSummary));
                InvalidateAllCommands();
            }
        }
    }

    /// <summary>
    /// 検出された入力テキストの文字コード名を取得します
    /// </summary>
    public string DetectedEncodingName
    {
        get => _detectedEncodingName;
        private set
        {
            if (SetProperty(ref _detectedEncodingName, value))
            {
                OnPropertyChanged(nameof(InputTextSummary));
            }
        }
    }

    /// <summary>
    /// 非 UTF-8 検出時の警告メッセージを取得します
    /// </summary>
    public string EncodingWarningMessage
    {
        get => _encodingWarningMessage;
        private set => SetProperty(ref _encodingWarningMessage, value);
    }

    /// <summary>
    /// 開始前バリデーションのインラインエラーメッセージを取得します
    /// </summary>
    public string ValidationErrorMessage
    {
        get => _validationErrorMessage;
        private set
        {
            if (SetProperty(ref _validationErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationError));
            }
        }
    }

    /// <summary>
    /// 開始前バリデーションエラーが表示中かどうかを取得します
    /// </summary>
    public bool HasValidationError => !string.IsNullOrEmpty(_validationErrorMessage);

    /// <summary>
    /// 現在アクティブなローカル音声認識モデル情報を取得します
    /// </summary>
    public LocalSpeechModelInfo ActiveModelInfo
    {
        get => _activeModelInfo;
        private set
        {
            if (SetProperty(ref _activeModelInfo, value))
            {
                OnPropertyChanged(nameof(IsModelAvailable));
                OnPropertyChanged(nameof(ActiveModelDisplay));
                OnPropertyChanged(nameof(ModelStatusBadgeText));
                NotifyVisualStatesChanged();
            }
        }
    }

    /// <summary>
    /// ローカルモデルが利用可能かどうかを取得します
    /// </summary>
    public bool IsModelAvailable => _activeModelInfo.IsAvailable;

    /// <summary>
    /// モデルステータスバーに表示するモデル名を取得します
    /// </summary>
    public string ActiveModelDisplay => _activeModelInfo.DisplayName;

    /// <summary>
    /// モデル状態バッジ文字列を取得します
    /// </summary>
    public string ModelStatusBadgeText => _isModelDownloading
        ? "DL中"
        : (_activeModelInfo.IsAvailable ? "準備完了" : "未配置");

    /// <summary>
    /// モデルダウンロードを実行中かどうかを取得します
    /// </summary>
    public bool IsModelDownloading
    {
        get => _isModelDownloading;
        private set
        {
            if (SetProperty(ref _isModelDownloading, value))
            {
                OnPropertyChanged(nameof(ModelStatusBadgeText));
                OnPropertyChanged(nameof(IsBusy));
                NotifyVisualStatesChanged();
            }
        }
    }

    /// <summary>
    /// モデルダウンロードの進捗率（0〜100）を取得します
    /// </summary>
    public double ModelDownloadProgress
    {
        get => _modelDownloadProgress;
        private set => SetProperty(ref _modelDownloadProgress, value);
    }

    /// <summary>
    /// モデルダウンロード中のステータスメッセージを取得します
    /// </summary>
    public string ModelDownloadStatusMessage
    {
        get => _modelDownloadStatusMessage;
        private set => SetProperty(ref _modelDownloadStatusMessage, value);
    }

    /// <summary>
    /// 選択中の音声認識言語オプションを取得または設定します
    /// </summary>
    public LrcLanguageOption SelectedLanguageOption
    {
        get => _selectedLanguageOption;
        set => SetProperty(ref _selectedLanguageOption, value ?? LanguageOptions[0]);
    }

    /// <summary>
    /// LRC 生成または音声文字起こしを実行中かどうかを取得します
    /// </summary>
    public bool IsGenerating
    {
        get => _isGenerating;
        private set
        {
            if (SetProperty(ref _isGenerating, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                NotifyVisualStatesChanged();
            }
        }
    }

    /// <summary>
    /// キャンセル要求を発行して中断完了を待機中かどうかを取得します
    /// </summary>
    public bool IsCancelling
    {
        get => _isCancelling;
        private set
        {
            if (SetProperty(ref _isCancelling, value))
            {
                InvalidateAllCommands();
            }
        }
    }

    /// <summary>
    /// 生成またはモデルダウンロードの非同期処理が稼働中かどうかを取得します
    /// </summary>
    public bool IsBusy => _isGenerating || _isModelDownloading;

    /// <summary>
    /// 生成・文字起こしの進捗率（0〜100）を取得します
    /// </summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>
    /// 生成・文字起こしのステータスメッセージを取得します
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// 現在の解析フェーズ表示ラベルを取得します
    /// </summary>
    public string CurrentPhaseLabel
    {
        get => _currentPhaseLabel;
        private set => SetProperty(ref _currentPhaseLabel, value);
    }

    /// <summary>
    /// 生成された同期歌詞ドキュメントを取得します
    /// </summary>
    public LyricsDocument? GeneratedDocument
    {
        get => _generatedDocument;
        private set
        {
            if (SetProperty(ref _generatedDocument, value))
            {
                OnPropertyChanged(nameof(HasResult));
                NotifyVisualStatesChanged();
            }
        }
    }

    /// <summary>
    /// プレビュー用の LRC 形式全文文字列を取得します
    /// </summary>
    public string PreviewLrcText
    {
        get => _previewLrcText;
        private set => SetProperty(ref _previewLrcText, value);
    }

    /// <summary>
    /// 生成結果を保持しているかどうかを取得します
    /// </summary>
    public bool HasResult => _generatedDocument is not null && _generatedDocument.HasVisibleLyrics;

    /// <summary>
    /// プレビューへ適用中の累計全体オフセット（秒）を取得します
    /// </summary>
    public double TotalOffsetSeconds
    {
        get => _totalOffsetSeconds;
        private set
        {
            if (SetProperty(ref _totalOffsetSeconds, value))
            {
                OnPropertyChanged(nameof(OffsetDisplayText));
            }
        }
    }

    /// <summary>
    /// 全体オフセットの表示テキスト（例: <c>補正: +0.2s</c>）を取得します
    /// </summary>
    public string OffsetDisplayText => string.Create(
        CultureInfo.InvariantCulture,
        $"補正: {(_totalOffsetSeconds >= 0 ? "+" : string.Empty)}{_totalOffsetSeconds:0.0}s");

    /// <summary>
    /// 既定の `.lrc` 保存先パスを取得します
    /// </summary>
    public string DestinationFilePath
    {
        get
        {
            if (_targetTrack is null || string.IsNullOrWhiteSpace(_targetTrack.FilePath))
            {
                return string.Empty;
            }

            try
            {
                return _generationService.ResolveDefaultLrcPath(_targetTrack);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// 直近で保存に成功した `.lrc` ファイルパスを取得します
    /// </summary>
    public string? SavedFilePath
    {
        get => _savedFilePath;
        private set
        {
            if (SetProperty(ref _savedFilePath, value))
            {
                OnPropertyChanged(nameof(HasSavedFile));
                InvalidateAllCommands();
            }
        }
    }

    /// <summary>
    /// 保存済みファイルが存在するかどうかを取得します
    /// </summary>
    public bool HasSavedFile => !string.IsNullOrWhiteSpace(_savedFilePath);

    /// <summary>
    /// 保存完了メッセージを取得します
    /// </summary>
    public string SaveCompletedMessage
    {
        get => _saveCompletedMessage;
        private set
        {
            if (SetProperty(ref _saveCompletedMessage, value))
            {
                OnPropertyChanged(nameof(HasSaveCompletedMessage));
            }
        }
    }

    /// <summary>
    /// 保存完了メッセージが表示可能かどうかを取得します
    /// </summary>
    public bool HasSaveCompletedMessage => !string.IsNullOrEmpty(_saveCompletedMessage);

    /// <summary>
    /// 既存 `.lrc` 上書き確認インラインバーを表示すべきかどうかを取得します
    /// </summary>
    public bool IsOverwriteConfirmVisible
    {
        get => _isOverwriteConfirmVisible;
        private set
        {
            if (SetProperty(ref _isOverwriteConfirmVisible, value))
            {
                InvalidateAllCommands();
            }
        }
    }

    /// <summary>
    /// 既存 `.lrc` 上書き確認メッセージを取得します
    /// </summary>
    public string OverwriteWarningMessage
    {
        get => _overwriteWarningMessage;
        private set => SetProperty(ref _overwriteWarningMessage, value);
    }

    /// <summary>
    /// エラー状態かどうかを取得します
    /// </summary>
    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (SetProperty(ref _hasError, value))
            {
                NotifyVisualStatesChanged();
            }
        }
    }

    /// <summary>
    /// エラー状態の見出しを取得します
    /// </summary>
    public string ErrorTitle
    {
        get => _errorTitle;
        private set => SetProperty(ref _errorTitle, value);
    }

    /// <summary>
    /// エラー状態の詳細メッセージを取得します
    /// </summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    // 6 排他視覚状態プロパティ（LRC自動生成UI仕様書 §3.3・§7.2 準拠）

    /// <summary>モデルダウンロード中ビューを表示すべきかどうかを取得します</summary>
    public bool IsModelDownloadingState => _isModelDownloading;

    /// <summary>生成実行中ビューを表示すべきかどうかを取得します</summary>
    public bool IsGeneratingState => !_isModelDownloading && _isGenerating;

    /// <summary>エラー状態ビューを表示すべきかどうかを取得します</summary>
    public bool IsErrorState => !_isModelDownloading && !_isGenerating && _hasError;

    /// <summary>モデル未配置ガイドビューを表示すべきかどうかを取得します</summary>
    public bool IsModelMissingState => !_isModelDownloading && !_isGenerating && !_hasError && !_activeModelInfo.IsAvailable;

    /// <summary>結果プレビュー・微調整ビューを表示すべきかどうかを取得します</summary>
    public bool IsPreviewState => !_isModelDownloading && !_isGenerating && !_hasError && _activeModelInfo.IsAvailable && HasResult;

    /// <summary>入力・待機ビューを表示すべきかどうかを取得します</summary>
    public bool IsInputState => !_isModelDownloading && !_isGenerating && !_hasError && _activeModelInfo.IsAvailable && !HasResult;

    // 公開コマンド群

    /// <summary>標準ローカルモデルをダウンロードするコマンドを取得します</summary>
    public ICommand DownloadDefaultModelCommand => _downloadDefaultModelCommand;

    /// <summary>配置済みローカルモデル（.bin）を指定するコマンドを取得します</summary>
    public ICommand SelectCustomModelFileCommand => _selectCustomModelFileCommand;

    /// <summary>モデルダウンロードをキャンセルするコマンドを取得します</summary>
    public ICommand CancelModelDownloadCommand => _cancelModelDownloadCommand;

    /// <summary>入力歌詞テキストファイル（.txt）を選択するコマンドを取得します</summary>
    public ICommand SelectTextFileCommand => _selectTextFileCommand;

    /// <summary>選択中の入力テキストファイルをクリアするコマンドを取得します</summary>
    public ICommand ClearTextFileCommand => _clearTextFileCommand;

    /// <summary>非 UTF-8 テキストの UTF-8 変換を承諾するコマンドを取得します</summary>
    public ICommand ConfirmEncodingConversionCommand => _confirmEncodingConversionCommand;

    /// <summary>非 UTF-8 テキストの変換をキャンセルして選択解除するコマンドを取得します</summary>
    public ICommand CancelEncodingConversionCommand => _cancelEncodingConversionCommand;

    /// <summary>テキストファイル（.txt）から LRC を生成するコマンドを取得します</summary>
    public ICommand GenerateLrcCommand => _generateLrcCommand;

    /// <summary>テキストなしで楽曲音声から直接文字起こしして LRC を生成するコマンドを取得します</summary>
    public ICommand TranscribeAudioCommand => _transcribeAudioCommand;

    /// <summary>生成・文字起こし処理をキャンセルするコマンドを取得します</summary>
    public ICommand CancelGenerationCommand => _cancelGenerationCommand;

    /// <summary>プレビュー全行のタイムスタンプを指定秒数オフセット調整するコマンドを取得します</summary>
    public ICommand AdjustOffsetCommand => _adjustOffsetCommand;

    /// <summary>プレビュー全体オフセットを 0.0 秒へリセットするコマンドを取得します</summary>
    public ICommand ResetOffsetCommand => _resetOffsetCommand;

    /// <summary>プレビューを破棄して入力画面へ戻り再生成するコマンドを取得します</summary>
    public ICommand RegenerateCommand => _regenerateCommand;

    /// <summary>プレビュー行クリック時に対象楽曲の該当タイムスタンプへシークするコマンドを取得します</summary>
    public ICommand SeekToPreviewLineCommand => _seekToPreviewLineCommand;

    /// <summary>生成した LRC をファイルへ保存するコマンドを取得します</summary>
    public ICommand SaveLrcCommand => _saveLrcCommand;

    /// <summary>既存 .lrc ファイルへの上書き保存を承諾実行するコマンドを取得します</summary>
    public ICommand ConfirmOverwriteSaveCommand => _confirmOverwriteSaveCommand;

    /// <summary>既存 .lrc 上書き確認バーを閉じるコマンドを取得します</summary>
    public ICommand CancelOverwriteSaveCommand => _cancelOverwriteSaveCommand;

    /// <summary>保存先フォルダをエクスプローラーで開くコマンドを取得します</summary>
    public ICommand OpenSavedFolderCommand => _openSavedFolderCommand;

    /// <summary>右側タブ下部を同期歌詞表示パネルへ戻すコマンドを取得します</summary>
    public ICommand BackToLyricsCommand => _backToLyricsCommand;

    /// <summary>LRC 自動生成パネルを閉じるコマンドを取得します</summary>
    public ICommand CloseGeneratorCommand => _closeGeneratorCommand;

    /// <summary>エラー状態から直前の処理を再試行するコマンドを取得します</summary>
    public ICommand RetryErrorCommand => _retryErrorCommand;

    /// <summary>エラー状態から適切な待機画面（モデル未配置または入力画面）へ戻るコマンドを取得します</summary>
    public ICommand ReturnFromErrorCommand => _returnFromErrorCommand;

    /// <summary>
    /// <see cref="LrcGeneratorViewModel"/> の新しいインスタンスを初期化します
    /// </summary>
    /// <param name="generationService">LRC 自動生成アプリケーションサービス</param>
    /// <param name="audioService">オーディオ再生サービス（プレビュー行クリックシーク連携用）</param>
    /// <param name="uiDispatcher">UI スレッドディスパッチャー</param>
    public LrcGeneratorViewModel(
        LrcGenerationApplicationService generationService,
        IAudioService audioService,
        IUiDispatcher uiDispatcher)
        : this(generationService, audioService, uiDispatcher, null, null, null)
    {
    }

    /// <summary>
    /// テスト用ダイアログ・フォルダ起動デリゲートを注入して <see cref="LrcGeneratorViewModel"/> を初期化します
    /// </summary>
    /// <param name="generationService">LRC 自動生成アプリケーションサービス</param>
    /// <param name="audioService">オーディオ再生サービス</param>
    /// <param name="uiDispatcher">UI スレッドディスパッチャー</param>
    /// <param name="selectTextFileFunc">`.txt` ファイル選択デリゲート（任意）</param>
    /// <param name="selectModelFileFunc">`.bin` モデルファイル選択デリゲート（任意）</param>
    /// <param name="openFolderAction">保存先フォルダを開くアクション（任意）</param>
    internal LrcGeneratorViewModel(
        LrcGenerationApplicationService generationService,
        IAudioService audioService,
        IUiDispatcher uiDispatcher,
        Func<string?>? selectTextFileFunc,
        Func<string?>? selectModelFileFunc,
        Action<string>? openFolderAction)
    {
        _generationService = generationService ?? throw new ArgumentNullException(nameof(generationService));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _selectTextFileFunc = selectTextFileFunc ?? ShowSelectTextFileDialog;
        _selectModelFileFunc = selectModelFileFunc ?? ShowSelectModelFileDialog;
        _openFolderAction = openFolderAction ?? OpenFolderInExplorer;

        _activeModelInfo = _generationService.GetActiveModelInfo();
        _selectedLanguageOption = LanguageOptions[0];

        _downloadDefaultModelCommand = new GeneratorCommand(_ => _ = DownloadDefaultModelAsync(), _ => !IsBusy);
        _selectCustomModelFileCommand = new GeneratorCommand(_ => SelectCustomModelFile(), _ => !IsBusy);
        _cancelModelDownloadCommand = new GeneratorCommand(_ => CancelActiveOperation(), _ => IsModelDownloading && !IsCancelling);

        _selectTextFileCommand = new GeneratorCommand(_ => _ = SelectTextFileAsync(), _ => !IsBusy && IsModelAvailable);
        _clearTextFileCommand = new GeneratorCommand(_ => ClearTextFile(), _ => !IsBusy && HasInputTextFile);
        _confirmEncodingConversionCommand = new GeneratorCommand(_ => ConfirmEncodingConversion(), _ => IsEncodingWarningVisible && !IsBusy);
        _cancelEncodingConversionCommand = new GeneratorCommand(_ => CancelEncodingConversion(), _ => IsEncodingWarningVisible && !IsBusy);

        _generateLrcCommand = new GeneratorCommand(
            _ => _ = GenerateFromTextAsync(),
            _ => !IsBusy && IsModelAvailable && _targetTrack is not null && HasInputTextFile && !IsEncodingWarningVisible);
        _transcribeAudioCommand = new GeneratorCommand(
            _ => _ = TranscribeFromAudioAsync(),
            _ => !IsBusy && IsModelAvailable && _targetTrack is not null);
        _cancelGenerationCommand = new GeneratorCommand(_ => CancelActiveOperation(), _ => IsGenerating && !IsCancelling);

        _adjustOffsetCommand = new GeneratorCommand(AdjustOffset, _ => HasResult && !IsBusy);
        _resetOffsetCommand = new GeneratorCommand(_ => ResetOffset(), _ => HasResult && !IsBusy);
        _regenerateCommand = new GeneratorCommand(_ => ResetResultToInput(), _ => HasResult && !IsBusy);
        _seekToPreviewLineCommand = new GeneratorCommand(SeekToPreviewLine, _ => HasResult);

        _saveLrcCommand = new GeneratorCommand(_ => _ = SaveLrcRequestedAsync(), _ => HasResult && !IsBusy && !IsOverwriteConfirmVisible);
        _confirmOverwriteSaveCommand = new GeneratorCommand(_ => _ = ExecuteSaveAsync(overwrite: true), _ => HasResult && !IsBusy && IsOverwriteConfirmVisible);
        _cancelOverwriteSaveCommand = new GeneratorCommand(_ => CancelOverwriteConfirm(), _ => IsOverwriteConfirmVisible);
        _openSavedFolderCommand = new GeneratorCommand(_ => OpenSavedFolder(), _ => HasSavedFile);

        _backToLyricsCommand = new GeneratorCommand(_ => BackToLyricsRequested?.Invoke(), _ => !IsBusy);
        _closeGeneratorCommand = new GeneratorCommand(_ => CloseRequested?.Invoke(), _ => !IsBusy);
        _retryErrorCommand = new GeneratorCommand(_ => _ = RetryLastFailedOperationAsync(), _ => IsErrorState);
        _returnFromErrorCommand = new GeneratorCommand(_ => ReturnFromError(), _ => IsErrorState);
    }

    /// <summary>
    /// 対象楽曲を設定し、必要に応じて生成状態を初期化します（非同期処理稼働中は現在処理を維持します）
    /// </summary>
    /// <param name="track">対象楽曲</param>
    public void SetTargetTrack(Track? track)
    {
        if (IsBusy)
        {
            return;
        }

        bool isSameTrack = _targetTrack is not null
            && track is not null
            && string.Equals(_targetTrack.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase);

        TargetTrack = track;
        RefreshModelInfo();

        if (!isSameTrack)
        {
            ClearResultState();
            ClearErrorState();
            ValidationErrorMessage = string.Empty;
            if (_targetTrack is not null && !string.IsNullOrWhiteSpace(_targetTrack.FilePath) && !File.Exists(_targetTrack.FilePath))
            {
                ValidationErrorMessage = "対象楽曲の音声ファイルが見つかりません。ファイルの配置を確認してください。";
            }
        }
    }

    /// <summary>
    /// ローカルモデルの配置状態を再取得して画面へ反映します
    /// </summary>
    public void RefreshModelInfo()
    {
        ActiveModelInfo = _generationService.GetActiveModelInfo();
    }

    /// <summary>
    /// ドラッグ＆ドロップまたはファイル選択で指定されたテキストファイルを検査し、UTF-8 / 非 UTF-8 状態を更新します
    /// </summary>
    /// <param name="textFilePath">選択されたテキストファイルパス</param>
    /// <returns>非同期タスク</returns>
    public async Task InspectAndSelectTextFileAsync(string textFilePath)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(textFilePath))
        {
            return;
        }

        ValidationErrorMessage = string.Empty;
        Result<TextEncodingInspectionResult> inspectionResult = await _generationService
            .InspectLyricsTextAsync(textFilePath)
            .ConfigureAwait(false);

        _uiDispatcher.Invoke(() =>
        {
            if (inspectionResult.IsFailure)
            {
                ClearTextFile();
                ValidationErrorMessage = inspectionResult.ErrorMessage;
                return;
            }

            TextEncodingInspectionResult inspection = inspectionResult.Value;
            _inputTextLineCount = inspection.NonEmptyLineCount;
            DetectedEncodingName = inspection.DetectedEncodingName;
            InputTextFilePath = textFilePath;

            if (inspection.RequiresConversion)
            {
                _isEncodingConversionApproved = false;
                EncodingWarningMessage =
                    $"文字コード {inspection.DetectedEncodingName} が検出されました。標準入力は UTF-8 です。内部処理用に UTF-8 へ変換して続行しますか？（元の .txt ファイル自体は上書きされません）";
                IsEncodingWarningVisible = true;
            }
            else
            {
                _isEncodingConversionApproved = false;
                EncodingWarningMessage = string.Empty;
                IsEncodingWarningVisible = false;
            }
        });
    }

    /// <summary>
    /// 標準ローカルモデルの非同期ダウンロードを実行します
    /// </summary>
    /// <returns>非同期タスク</returns>
    public async Task DownloadDefaultModelAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ClearErrorState();
        ModelDownloadProgress = 0;
        ModelDownloadStatusMessage = "無料 Whisper モデル (base) をダウンロードしています...";
        IsModelDownloading = true;
        IsCancelling = false;

        CancellationTokenSource cts = CreateOperationCts();
        try
        {
            var progress = new DirectProgress<double>(p =>
            {
                double percent = Math.Round(Math.Clamp(p * 100.0, 0.0, 100.0), 1);
                _uiDispatcher.BeginInvoke(() =>
                {
                    if (_disposed)
                    {
                        return;
                    }

                    ModelDownloadProgress = percent;
                    ModelDownloadStatusMessage = string.Create(
                        CultureInfo.InvariantCulture,
                        $"モデルをダウンロードしています... ({percent:0}%)");
                });
            });

            Result<LocalSpeechModelInfo> result = await _generationService
                .DownloadModelAsync(progress: progress, cancellationToken: cts.Token)
                .ConfigureAwait(false);

            _uiDispatcher.Invoke(() =>
            {
                IsModelDownloading = false;
                IsCancelling = false;
                RefreshModelInfo();

                if (result.IsFailure)
                {
                    SetError("モデルをダウンロードできませんでした", result.ErrorMessage, isModelDownloadError: true);
                }
            });
        }
        catch (OperationCanceledException)
        {
            _uiDispatcher.Invoke(() =>
            {
                IsModelDownloading = false;
                IsCancelling = false;
                RefreshModelInfo();
            });
        }
        finally
        {
            DisposeOperationCts(cts);
        }
    }

    /// <summary>
    /// モード A: 選択済みテキストファイル（.txt）から LRC を非同期生成します
    /// </summary>
    /// <returns>非同期タスク</returns>
    public async Task GenerateFromTextAsync()
    {
        if (IsBusy || _targetTrack is null || string.IsNullOrWhiteSpace(_inputTextFilePath) || IsEncodingWarningVisible)
        {
            return;
        }

        await ExecuteGenerationAsync(LrcGenerationMode.TextAlignment).ConfigureAwait(false);
    }

    /// <summary>
    /// モード B: テキストファイルを使わずに楽曲音声から直接文字起こしして LRC を非同期生成します
    /// </summary>
    /// <returns>非同期タスク</returns>
    public async Task TranscribeFromAudioAsync()
    {
        if (IsBusy || _targetTrack is null)
        {
            return;
        }

        await ExecuteGenerationAsync(LrcGenerationMode.AudioTranscription).ConfigureAwait(false);
    }

    /// <summary>
    /// 保存ボタン押下時のフローを実行します（既存 `.lrc` がある場合は上書き確認バーを表示）
    /// </summary>
    /// <returns>非同期タスク</returns>
    public async Task SaveLrcRequestedAsync()
    {
        if (!HasResult || _targetTrack is null || IsBusy)
        {
            return;
        }

        if (_generationService.LrcFileExists(_targetTrack))
        {
            string fileName = Path.GetFileName(DestinationFilePath);
            OverwriteWarningMessage = $"同名の .lrc ファイルが既に存在します: {fileName}。上書き保存しますか？";
            IsOverwriteConfirmVisible = true;
            return;
        }

        await ExecuteSaveAsync(overwrite: false).ConfigureAwait(false);
    }

    /// <summary>
    /// 現在のプレビュー行から最新の <see cref="LyricsDocument"/> を再構築し `.lrc` を保存します
    /// </summary>
    /// <param name="overwrite">既存ファイルの上書きを許可するかどうか</param>
    /// <returns>非同期タスク</returns>
    public async Task ExecuteSaveAsync(bool overwrite)
    {
        if (!HasResult || _targetTrack is null || IsBusy)
        {
            return;
        }

        LyricsDocument currentDoc = RebuildDocumentFromPreviewLines();
        Result<string> saveResult = await _generationService
            .SaveLrcAsync(_targetTrack, currentDoc, destinationPath: null, overwrite: overwrite)
            .ConfigureAwait(false);

        _uiDispatcher.Invoke(() =>
        {
            IsOverwriteConfirmVisible = false;
            OverwriteWarningMessage = string.Empty;

            if (saveResult.IsFailure)
            {
                ValidationErrorMessage = saveResult.ErrorMessage;
                return;
            }

            ValidationErrorMessage = string.Empty;
            SavedFilePath = saveResult.Value;
            SaveCompletedMessage = $"保存しました: {Path.GetFileName(saveResult.Value)}";
            LrcSaved?.Invoke(saveResult.Value);
        });
    }

    /// <summary>
    /// 非同期処理用のキャンセレーショントークン等を解放します
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelActiveOperation();
        lock (_ctsLock)
        {
            _operationCts?.Dispose();
            _operationCts = null;
        }
    }

    private async Task SelectTextFileAsync()
    {
        string? selectedPath = _selectTextFileFunc();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            await InspectAndSelectTextFileAsync(selectedPath).ConfigureAwait(false);
        }
    }

    private void SelectCustomModelFile()
    {
        string? selectedPath = _selectModelFileFunc();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        Result result = _generationService.SetCustomModelPath(selectedPath);
        if (result.IsFailure)
        {
            ValidationErrorMessage = result.ErrorMessage;
            return;
        }

        ValidationErrorMessage = string.Empty;
        ClearErrorState();
        RefreshModelInfo();
    }

    private void ClearTextFile()
    {
        _inputTextLineCount = 0;
        _isEncodingConversionApproved = false;
        DetectedEncodingName = string.Empty;
        EncodingWarningMessage = string.Empty;
        IsEncodingWarningVisible = false;
        InputTextFilePath = null;
    }

    private void ConfirmEncodingConversion()
    {
        _isEncodingConversionApproved = true;
        EncodingWarningMessage = string.Empty;
        IsEncodingWarningVisible = false;
    }

    private void CancelEncodingConversion()
    {
        ClearTextFile();
    }

    private async Task ExecuteGenerationAsync(LrcGenerationMode mode)
    {
        if (_targetTrack is null)
        {
            return;
        }

        _lastExecutedMode = mode;
        ClearErrorState();
        ClearResultState();
        ValidationErrorMessage = string.Empty;

        Progress = 0;
        _lastReportedPercent = -10.0;
        _lastReportedPhase = AlignmentPhase.DecodingAudio;
        CurrentPhaseLabel = mode == LrcGenerationMode.AudioTranscription
            ? "音声からの文字起こしを準備中..."
            : "テキストからの LRC 生成を準備中...";
        StatusMessage = TargetTrackDisplay;
        IsCancelling = false;
        IsGenerating = true;

        CancellationTokenSource cts = CreateOperationCts();
        string? languageCode = string.Equals(_selectedLanguageOption.Code, "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : _selectedLanguageOption.Code;

        try
        {
            var progressReporter = new DirectProgress<AlignmentProgress>(OnAlignmentProgressReported);
            Result<AlignmentResult> result = mode == LrcGenerationMode.TextAlignment
                ? await _generationService
                    .GenerateLrcAsync(
                        _targetTrack,
                        _inputTextFilePath!,
                        _isEncodingConversionApproved,
                        progressReporter,
                        languageCode,
                        cts.Token)
                    .ConfigureAwait(false)
                : await _generationService
                    .TranscribeAudioToLrcAsync(
                        _targetTrack,
                        progressReporter,
                        languageCode,
                        cts.Token)
                    .ConfigureAwait(false);

            _uiDispatcher.Invoke(() =>
            {
                IsGenerating = false;
                IsCancelling = false;

                if (result.IsFailure)
                {
                    SetError("LRC を生成できませんでした", result.ErrorMessage, isModelDownloadError: false);
                    return;
                }

                ApplyGenerationResult(result.Value);
            });
        }
        catch (OperationCanceledException)
        {
            _uiDispatcher.Invoke(() =>
            {
                IsGenerating = false;
                IsCancelling = false;
                Progress = 0;
                StatusMessage = string.Empty;
                CurrentPhaseLabel = string.Empty;
            });
        }
        finally
        {
            DisposeOperationCts(cts);
        }
    }

    private void OnAlignmentProgressReported(AlignmentProgress update)
    {
        double percent = Math.Round(update.Percentage * 100.0, 1);
        bool phaseChanged = update.Phase != _lastReportedPhase;
        if (!phaseChanged && Math.Abs(percent - _lastReportedPercent) < PROGRESS_REPORT_MIN_DELTA && percent < 100.0)
        {
            return;
        }

        _lastReportedPhase = update.Phase;
        _lastReportedPercent = percent;

        string phaseLabel = update.Phase switch
        {
            AlignmentPhase.DecodingAudio => "音声デコード中 (1/5)",
            AlignmentPhase.LoadingModel => "モデル読み込み中 (2/5)",
            AlignmentPhase.Transcribing => "音声認識・文字起こし中 (3/5)",
            AlignmentPhase.AligningLyrics => "歌詞タイミング合わせ中 (4/5)",
            AlignmentPhase.FormattingLrc => "LRC 整形中 (5/5)",
            AlignmentPhase.Completed => "完了",
            _ => "処理中...",
        };

        _uiDispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            Progress = percent;
            CurrentPhaseLabel = phaseLabel;
            StatusMessage = update.Message;
        });
    }

    private void ApplyGenerationResult(AlignmentResult result)
    {
        PreviewLines.Clear();
        for (int i = 0; i < result.Document.Lines.Count; i++)
        {
            LyricLine line = result.Document.Lines[i];
            PreviewLines.Add(new LrcPreviewLineViewModel(
                i,
                line.Timestamp ?? TimeSpan.Zero,
                line.Text,
                OnPreviewLineEdited));
        }

        TotalOffsetSeconds = 0.0;
        GeneratedDocument = result.Document;
        PreviewLrcText = result.LrcContent;
        SavedFilePath = null;
        SaveCompletedMessage = string.Empty;
        IsOverwriteConfirmVisible = false;
        OverwriteWarningMessage = string.Empty;
    }

    private void AdjustOffset(object? parameter)
    {
        if (!HasResult || parameter is null)
        {
            return;
        }

        if (!double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double deltaSeconds))
        {
            return;
        }

        TotalOffsetSeconds = Math.Round(_totalOffsetSeconds + deltaSeconds, 2);
        foreach (LrcPreviewLineViewModel line in PreviewLines)
        {
            line.ApplyGlobalOffset(_totalOffsetSeconds);
        }

        RefreshGeneratedDocumentFromPreviewLines();
    }

    private void ResetOffset()
    {
        if (!HasResult)
        {
            return;
        }

        TotalOffsetSeconds = 0.0;
        foreach (LrcPreviewLineViewModel line in PreviewLines)
        {
            line.ApplyGlobalOffset(0.0);
        }

        RefreshGeneratedDocumentFromPreviewLines();
    }

    private void OnPreviewLineEdited(LrcPreviewLineViewModel editedLine)
    {
        _ = editedLine;
        RefreshGeneratedDocumentFromPreviewLines();
    }

    private void RefreshGeneratedDocumentFromPreviewLines()
    {
        LyricsDocument updated = RebuildDocumentFromPreviewLines();
        _generatedDocument = updated;
        OnPropertyChanged(nameof(GeneratedDocument));
        PreviewLrcText = _generationService.FormatDocument(updated);
        SaveCompletedMessage = string.Empty;
    }

    private LyricsDocument RebuildDocumentFromPreviewLines()
    {
        LyricLine[] lines = PreviewLines
            .Select((vm, idx) => vm.ToLyricLine(idx))
            .ToArray();

        return new LyricsDocument(
            lines: lines,
            isSynced: true,
            title: _generatedDocument?.Title ?? _targetTrack?.Title,
            artist: _generatedDocument?.Artist ?? _targetTrack?.Artist,
            album: _generatedDocument?.Album ?? _targetTrack?.Album,
            author: _generatedDocument?.Author,
            sourceType: LyricsSourceType.LrcFile);
    }

    private void SeekToPreviewLine(object? parameter)
    {
        if (parameter is not LrcPreviewLineViewModel line || line.IsEditing)
        {
            return;
        }

        Track? playingTrack = _audioService.CurrentTrack;
        if (_targetTrack is null
            || playingTrack is null
            || !string.Equals(_targetTrack.FilePath, playingTrack.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TimeSpan totalTime = _audioService.TotalTime;
        if (totalTime <= TimeSpan.Zero)
        {
            return;
        }

        double ratio = Math.Clamp(line.Timestamp.TotalSeconds / totalTime.TotalSeconds, 0.0, 1.0);
        _audioService.SeekTo(ratio);
    }

    private void ResetResultToInput()
    {
        ClearResultState();
        ValidationErrorMessage = string.Empty;
    }

    private void ClearResultState()
    {
        PreviewLines.Clear();
        TotalOffsetSeconds = 0.0;
        GeneratedDocument = null;
        PreviewLrcText = string.Empty;
        SavedFilePath = null;
        SaveCompletedMessage = string.Empty;
        IsOverwriteConfirmVisible = false;
        OverwriteWarningMessage = string.Empty;
    }

    private void CancelOverwriteConfirm()
    {
        IsOverwriteConfirmVisible = false;
        OverwriteWarningMessage = string.Empty;
    }

    private void OpenSavedFolder()
    {
        if (string.IsNullOrWhiteSpace(_savedFilePath))
        {
            return;
        }

        _openFolderAction(_savedFilePath);
    }

    private void SetError(string title, string message, bool isModelDownloadError)
    {
        _isModelDownloadError = isModelDownloadError;
        ErrorTitle = title;
        ErrorMessage = message;
        HasError = true;
    }

    private void ClearErrorState()
    {
        _isModelDownloadError = false;
        ErrorTitle = string.Empty;
        ErrorMessage = string.Empty;
        HasError = false;
    }

    private async Task RetryLastFailedOperationAsync()
    {
        bool retryModelDownload = _isModelDownloadError;
        ClearErrorState();

        if (retryModelDownload)
        {
            await DownloadDefaultModelAsync().ConfigureAwait(false);
        }
        else if (_lastExecutedMode == LrcGenerationMode.TextAlignment && HasInputTextFile && !IsEncodingWarningVisible)
        {
            await GenerateFromTextAsync().ConfigureAwait(false);
        }
        else
        {
            await TranscribeFromAudioAsync().ConfigureAwait(false);
        }
    }

    private void ReturnFromError()
    {
        ClearErrorState();
        RefreshModelInfo();
    }

    private void CancelActiveOperation()
    {
        CancellationTokenSource? cts;
        lock (_ctsLock)
        {
            cts = _operationCts;
        }

        if (cts is null)
        {
            return;
        }

        IsCancelling = true;
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 完了直後の競合破棄は無視
        }
    }

    private CancellationTokenSource CreateOperationCts()
    {
        var cts = new CancellationTokenSource();
        lock (_ctsLock)
        {
            _operationCts?.Dispose();
            _operationCts = cts;
        }

        return cts;
    }

    private void DisposeOperationCts(CancellationTokenSource cts)
    {
        lock (_ctsLock)
        {
            if (ReferenceEquals(_operationCts, cts))
            {
                _operationCts = null;
            }
        }

        cts.Dispose();
    }

    private void NotifyVisualStatesChanged()
    {
        OnPropertyChanged(nameof(IsModelDownloadingState));
        OnPropertyChanged(nameof(IsGeneratingState));
        OnPropertyChanged(nameof(IsErrorState));
        OnPropertyChanged(nameof(IsModelMissingState));
        OnPropertyChanged(nameof(IsPreviewState));
        OnPropertyChanged(nameof(IsInputState));
        InvalidateAllCommands();
    }

    private void InvalidateAllCommands()
    {
        _downloadDefaultModelCommand.RaiseCanExecuteChanged();
        _selectCustomModelFileCommand.RaiseCanExecuteChanged();
        _cancelModelDownloadCommand.RaiseCanExecuteChanged();
        _selectTextFileCommand.RaiseCanExecuteChanged();
        _clearTextFileCommand.RaiseCanExecuteChanged();
        _confirmEncodingConversionCommand.RaiseCanExecuteChanged();
        _cancelEncodingConversionCommand.RaiseCanExecuteChanged();
        _generateLrcCommand.RaiseCanExecuteChanged();
        _transcribeAudioCommand.RaiseCanExecuteChanged();
        _cancelGenerationCommand.RaiseCanExecuteChanged();
        _adjustOffsetCommand.RaiseCanExecuteChanged();
        _resetOffsetCommand.RaiseCanExecuteChanged();
        _regenerateCommand.RaiseCanExecuteChanged();
        _seekToPreviewLineCommand.RaiseCanExecuteChanged();
        _saveLrcCommand.RaiseCanExecuteChanged();
        _confirmOverwriteSaveCommand.RaiseCanExecuteChanged();
        _cancelOverwriteSaveCommand.RaiseCanExecuteChanged();
        _openSavedFolderCommand.RaiseCanExecuteChanged();
        _backToLyricsCommand.RaiseCanExecuteChanged();
        _closeGeneratorCommand.RaiseCanExecuteChanged();
        _retryErrorCommand.RaiseCanExecuteChanged();
        _returnFromErrorCommand.RaiseCanExecuteChanged();
    }

    private static string? ShowSelectTextFileDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "歌詞テキストファイルを選択",
            Filter = "テキストファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string? ShowSelectModelFileDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "ローカル Whisper モデルファイル (.bin) を選択",
            Filter = "GGML モデルファイル (*.bin)|*.bin|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static void OpenFolderInExplorer(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                using var process = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
                {
                    UseShellExecute = true,
                });
            }
            else
            {
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    using var process = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"")
                    {
                        UseShellExecute = true,
                    });
                }
            }
        }
        catch
        {
            // エクスプローラー起動失敗はアプリ全体を落とさず無視
        }
    }

    private sealed class DirectProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private sealed class GeneratorCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute is null || canExecute(parameter);

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                execute(parameter);
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// LRC 自動生成における音声認識言語の選択肢を表すレコードです
/// </summary>
/// <param name="Code">言語コード（自動判定は <c>auto</c>、日本語は <c>ja</c>、英語は <c>en</c>）</param>
/// <param name="DisplayName">UI 表示ラベル</param>
public sealed record LrcLanguageOption(string Code, string DisplayName);
