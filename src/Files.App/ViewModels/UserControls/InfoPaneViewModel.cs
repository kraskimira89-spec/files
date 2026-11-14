// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.UserControls.FilePreviews;
using Files.App.ViewModels.Previews;
using Files.Shared.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Storage;
using WinRT;

namespace Files.App.ViewModels.UserControls
{
	public sealed partial class InfoPaneViewModel : ObservableObject, IDisposable
	{
		private IInfoPaneSettingsService infoPaneSettingsService { get; } = Ioc.Default.GetRequiredService<IInfoPaneSettingsService>();
		private IContentPageContext contentPageContext { get; } = Ioc.Default.GetRequiredService<IContentPageContext>();
		private DrivesViewModel drivesViewModel { get; } = Ioc.Default.GetRequiredService<DrivesViewModel>();

		private CancellationTokenSource? loadCancellationTokenSource;
		private Files.App.UserControls.Menus.FileTagsContextMenu? cachedTagsContextMenu;

		/// <summary>
		/// Value indicating if the info pane is on/off
		/// </summary>
		private bool isEnabled;
		public bool IsEnabled
		{
			get => isEnabled;
			set
			{
				infoPaneSettingsService.IsInfoPaneEnabled = value;

				SetProperty(ref isEnabled, value);
			}
		}

		/// <summary>
		/// Current selected item in the file list.
		/// TODO see about removing this and accessing it from the page context instead
		/// </summary>
		private ListedItem? selectedItem;
		public ListedItem? SelectedItem
		{
			get => selectedItem;
			set
			{
				if (selectedItem is not null)
					selectedItem.PropertyChanged -= SelectedItem_PropertyChanged;

				if (SetProperty(ref selectedItem, value))
				{
					UpdateTagsItems();
					SetDriveItem();
					OnPropertyChanged(nameof(LoadTagsList));

					if (value is not null)
						value.PropertyChanged += SelectedItem_PropertyChanged;
				}
			}
		}

		/// <summary>
		/// Current selected drive if any.
		/// </summary>
		private DriveItem? selectedDriveItem;
		public DriveItem? SelectedDriveItem
		{
			get => selectedDriveItem;
			set
			{
				SetProperty(ref selectedDriveItem, value);
			}
		}

		/// <summary>
		/// Enum indicating whether to show the details or preview tab
		/// </summary>
		public InfoPaneTabs SelectedTab
		{
			get => infoPaneSettingsService.SelectedTab;
			set
			{
				if (value != infoPaneSettingsService.SelectedTab)
				{
					infoPaneSettingsService.SelectedTab = value;
				}
			}
		}

		/// <summary>
		/// Enum indicating if details/preview are available
		/// </summary>
		private PreviewPaneStates previewPaneState;
		public PreviewPaneStates PreviewPaneState
		{
			get => previewPaneState;
			set
			{
				if (SetProperty(ref previewPaneState, value))
					OnPropertyChanged(nameof(LoadTagsList));
			}
		}

		private string? _DirectoryItemCount;
		public string? DirectoryItemCount
		{
			get => _DirectoryItemCount;
			set => SetProperty(ref _DirectoryItemCount, value);
		}

		/// <summary>
		/// Value indicating if the download cloud files option should be displayed
		/// </summary>
		private bool showCloudItemButton;
		public bool ShowCloudItemButton
		{
			get => showCloudItemButton;
			set => SetProperty(ref showCloudItemButton, value);
		}

		private UIElement? previewPaneContent;
		public UIElement? PreviewPaneContent
		{
			get => previewPaneContent;
			set
			{
				var oldType = previewPaneContent?.GetType()?.Name;
				var newType = value?.GetType()?.Name;
				App.Logger.LogDebug($"PreviewPaneContent changing: {oldType} -> {newType}");
				SetProperty(ref previewPaneContent, value);
			}
		}

		public bool LoadTagsList
			=> SelectedItem?.HasTags ?? false &&
			PreviewPaneState is PreviewPaneStates.NoPreviewAvailable ||
			PreviewPaneState is PreviewPaneStates.PreviewAndDetailsAvailable;

		public ObservableCollection<TagsListItem> Items { get; } = [];

		public InfoPaneViewModel()
		{
			infoPaneSettingsService.PropertyChanged += PreviewSettingsService_OnPropertyChangedEvent;
			contentPageContext.PropertyChanged += ContentPageContext_PropertyChanged;
			CloudDrivesManager.DataChanged += CloudDrivesManager_DataChanged;

			IsEnabled = infoPaneSettingsService.IsInfoPaneEnabled;
		}

		private void CloudDrivesManager_DataChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			_ = MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
			{
				if (SelectedItem is null)
					return;

				SetDriveItem();

				if (PreviewPaneState is PreviewPaneStates.PreviewAndDetailsAvailable && SelectedDriveItem is not null)
					PreviewPaneState = PreviewPaneStates.DriveStorageDetailsAvailable;
				else if (PreviewPaneState is PreviewPaneStates.DriveStorageDetailsAvailable && SelectedDriveItem is null)
					PreviewPaneState = PreviewPaneStates.PreviewAndDetailsAvailable;
			});
		}

		[DynamicWindowsRuntimeCast(typeof(Frame))]
		private async void ContentPageContext_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(IContentPageContext.Folder):
				case nameof(IContentPageContext.SelectedItem):
					try
					{
						ListedItem? tempSelectedItem = null;
						if (contentPageContext.SelectedItems.Count == 1)
							tempSelectedItem = contentPageContext.SelectedItems.First();

						// Don't update preview pane when the selected item changes too frequently
						const int delayBeforeUpdatingPreviewPane = 100;
						await Task.Delay(delayBeforeUpdatingPreviewPane);
						if (tempSelectedItem is not null && !tempSelectedItem.Equals(contentPageContext.SelectedItem))
							return;

						SelectedItem = tempSelectedItem;

						if (!App.AppModel.IsMainWindowClosed)
						{
							var shouldUpdatePreview = ((MainWindow.Instance.Content as Frame)?.Content as MainPage)?.ViewModel.ShouldPreviewPaneBeActive;
							if (shouldUpdatePreview == true)
								_ = UpdateSelectedItemPreviewAsync();
						}
					}
					catch (Exception ex)
					{
						// Handle exception in case WinUI Windows is closed
						// (see https://github.com/files-community/Files/issues/15599)
						App.Logger.LogWarning(ex, ex.Message);
					}

					break;
			}
		}

		private async Task LoadPreviewControlAsync(CancellationToken token, bool downloadItem)
		{
			var totalSw = Stopwatch.StartNew();
			var item = SelectedItem;
			if (item is null)
				throw new InvalidOperationException("The preview pane does not have a selected item.");

			if (item.IsHiddenItem && !item.ItemPath!.EndsWith('\\'))
			{
				PreviewPaneState = PreviewPaneStates.NoPreviewOrDetailsAvailable;

				PreviewPaneContent = null;
				return;
			}

			// Show the control immediately; finish loading under the same cancellation token.
			var createSw = Stopwatch.StartNew();
			var builtIn = CreateBuiltInPreviewControl(item, downloadItem);
			var createMs = createSw.ElapsedMilliseconds;

			if (token.IsCancellationRequested)
				return;

			if (builtIn is not null)
			{
				PreviewPaneContent = builtIn.Value.Control;
				PreviewPaneState = SelectedDriveItem is not null ? PreviewPaneStates.DriveStorageDetailsAvailable : PreviewPaneStates.PreviewAndDetailsAvailable;

				var loadSw = Stopwatch.StartNew();
				await builtIn.Value.LoadTask;
				App.Logger.LogDebug(
					"Preview.LoadPreviewControl: path={Path}, create={CreateMs}ms, loadTask={LoadMs}ms, total={TotalMs}ms, kind=builtIn",
					LogPathHelper.RedactPath(item.ItemPath),
					createMs,
					loadSw.ElapsedMilliseconds,
					totalSw.ElapsedMilliseconds);
				return;
			}

			// Unknown extensions: sniff as text before falling back to thumbnail-only preview.
			var textControl = await TextPreviewViewModel.TryLoadAsTextAsync(item);
			if (token.IsCancellationRequested)
				return;

			if (textControl is not null)
			{
				PreviewPaneContent = textControl;
				PreviewPaneState = SelectedDriveItem is not null ? PreviewPaneStates.DriveStorageDetailsAvailable : PreviewPaneStates.PreviewAndDetailsAvailable;
				App.Logger.LogDebug(
					"Preview.LoadPreviewControl: path={Path}, create={CreateMs}ms, total={TotalMs}ms, kind=text",
					LogPathHelper.RedactPath(item.ItemPath),
					createMs,
					totalSw.ElapsedMilliseconds);
				return;
			}

			var basicModel = new BasicPreviewViewModel(item);
			var control = new BasicPreview(basicModel);
			PreviewPaneContent = control;
			PreviewPaneState = SelectedDriveItem is not null ? PreviewPaneStates.DriveStorageDetailsAvailable : PreviewPaneStates.PreviewAndDetailsAvailable;

			var basicLoadSw = Stopwatch.StartNew();
			await basicModel.LoadAsync();
			App.Logger.LogDebug(
				"Preview.LoadPreviewControl: path={Path}, create={CreateMs}ms, loadTask={LoadMs}ms, total={TotalMs}ms, kind=basic",
				LogPathHelper.RedactPath(item.ItemPath),
				createMs,
				basicLoadSw.ElapsedMilliseconds,
				totalSw.ElapsedMilliseconds);
		}

		/// <summary>
		/// Creates the preview control without waiting for <see cref="BasePreviewModel.LoadAsync"/> so the pane can paint first.
		/// </summary>
		private (UserControl Control, Task LoadTask)? CreateBuiltInPreviewControl(ListedItem item, bool downloadItem)
		{
			ShowCloudItemButton = false;

			if (item.IsRecycleBinItem)
			{
				if (item.PrimaryItemAttribute == StorageItemTypes.Folder && !item.IsArchive)
				{
					var model = new FolderPreviewViewModel(item);
					return (new FolderPreview(model), model.LoadAsync());
				}
				else
				{
					var model = new BasicPreviewViewModel(item);
					return (new BasicPreview(model), model.LoadAsync());
				}
			}

			if (item.IsShortcut)
			{
				var model = new ShortcutPreviewViewModel(item);
				return (new BasicPreview(model), model.LoadAsync());
			}

			if (FileExtensionHelpers.IsBrowsableZipFile(item.FileExtension, out _))
			{
				var model = new ArchivePreviewViewModel(item);
				return (new BasicPreview(model), model.LoadAsync());
			}

			if (item.PrimaryItemAttribute == StorageItemTypes.Folder)
			{
				var model = new FolderPreviewViewModel(item);

				if (contentPageContext.SelectedItems.Count == 0)
					item.FileTags ??= FileTagsHelper.ReadFileTag(item.ItemPath!);

				return (new FolderPreview(model), model.LoadAsync());
			}

			if (item.FileExtension is null)
				return null;

			if (item.SyncStatusUI.SyncStatus is CloudDriveSyncStatus.FileOnline && !downloadItem)
			{
				ShowCloudItemButton = true;

				return null;
			}

			var ext = item.FileExtension.ToLowerInvariant();

			if (!item.IsFtpItem &&
				contentPageContext.PageType != ContentPageTypes.ZipFolder &&
				(FileExtensionHelpers.IsAudioFile(ext) || FileExtensionHelpers.IsVideoFile(ext)))
			{
				var model = new MediaPreviewViewModel(item);
				return (new MediaPreview(model), model.LoadAsync());
			}

			if (FileExtensionHelpers.IsMarkdownFile(ext))
			{
				var model = new MarkdownPreviewViewModel(item);
				return (new MarkdownPreview(model), model.LoadAsync());
			}

			if (FileExtensionHelpers.IsImagePreviewFile(ext))
			{
				var model = new ImagePreviewViewModel(item);
				return (new ImagePreview(model), model.LoadAsync());
			}

			// Prefer the built-in PDF renderer (progressive pages) over the slow Shell preview handler.
			if (FileExtensionHelpers.IsPdfFile(ext))
			{
				var model = new PDFPreviewViewModel(item);
				return (new PDFPreview(model), model.LoadAsync());
			}

			if (FileExtensionHelpers.IsTextFile(ext))
			{
				var model = new TextPreviewViewModel(item);
				return (new TextPreview(model), model.LoadAsync());
			}

			if (FileExtensionHelpers.IsRichTextFile(ext))
			{
				var model = new RichTextPreviewViewModel(item);
				return (new RichTextPreview(model), model.LoadAsync());
			}

			if (CodePreviewViewModel.IsCodeFile(ext))
			{
				var model = new CodePreviewViewModel(item);
				return (new CodePreview(model), model.LoadAsync());
			}

			// Read Office packages directly instead of spawning the slow Shell preview handler.
			if (ext is ".docx")
			{
				var model = new DocxPreviewViewModel(item);
				return (new TextPreview(model), model.LoadAsync());
			}

			if (ShellPreviewViewModel.FindPreviewHandlerFor(item.FileExtension, 0) is not null &&
				!FileExtensionHelpers.IsFontFile(item.FileExtension) &&
				!FileExtensionHelpers.IsExecutableFile(item.FileExtension))
			{
				var model = new ShellPreviewViewModel(item);
				return (new ShellPreview(model), model.LoadAsync());
			}

			// Text sniff still needs a full await before we know if a control exists.
			return null;
		}

		private int previewRequestVersion;

		public Task UpdateSelectedItemPreviewAsync(bool downloadItem = false)
		{
			CancelPreviewLoad();
			var version = ++previewRequestVersion;
			return App.UiDispatcher.EnqueueDeferredAsync(() =>
			{
				if (version != previewRequestVersion || App.AppModel.IsMainWindowClosed)
					return Task.CompletedTask;
				return UpdateSelectedItemPreviewCoreAsync(downloadItem);
			});
		}

		private async Task UpdateSelectedItemPreviewCoreAsync(bool downloadItem)
		{
			if (contentPageContext.PageType is ContentPageTypes.ReleaseNotes || contentPageContext.PageType is ContentPageTypes.Settings)
			{
				PreviewPaneState = PreviewPaneStates.NoPreviewOrDetailsAvailable;
				PreviewPaneContent = null;
				return;
			}
			else if (SelectedItem is not null && contentPageContext.SelectedItems.Count == 1)
			{
				SelectedItem?.FileDetails?.Clear();
				var token = CreatePreviewLoadToken();

				try
				{
					PreviewPaneState = PreviewPaneStates.LoadingPreview;

					if (SelectedTab == InfoPaneTabs.Preview ||
						SelectedItem?.PrimaryItemAttribute == StorageItemTypes.Folder)
					{
						await LoadPreviewControlAsync(token, downloadItem);
					}
					else
					{
						await LoadBasicPreviewAsync(token);
						return;
					}
				}
				catch (Exception e)
				{
					Debug.WriteLine(e);

					if (token.IsCancellationRequested)
						return;

					// If initial loading fails, attempt to load a basic preview (thumbnail and details only)
					// If that fails, revert to no preview/details available as long as the item is not a shortcut or folder
					if (SelectedItem is not null && !SelectedItem.IsShortcut && SelectedItem.PrimaryItemAttribute != StorageItemTypes.Folder)
					{
						await LoadBasicPreviewAsync(token);
						return;
					}

					PreviewPaneContent = null;
					PreviewPaneState = PreviewPaneStates.NoPreviewOrDetailsAvailable;
				}
			}
			else if (contentPageContext.SelectedItems.Count > 0)
			{
				PreviewPaneContent = null;
				PreviewPaneState = PreviewPaneStates.NoPreviewOrDetailsAvailable;
			}
			else
			{
				SelectedItem?.FileDetails?.Clear();
				var currentFolder = contentPageContext.Folder;

				if (currentFolder is null)
				{
					PreviewPaneContent = null;
					PreviewPaneState = PreviewPaneStates.NoItemSelected;
					return;
				}

				var token = CreatePreviewLoadToken();

				try
				{
					PreviewPaneState = PreviewPaneStates.LoadingPreview;

					SelectedItem = currentFolder;
					await LoadPreviewControlAsync(token, downloadItem);
				}
				catch (Exception e)
				{
					Debug.WriteLine(e);

					if (token.IsCancellationRequested)
						return;

					PreviewPaneContent = null;
					PreviewPaneState = PreviewPaneStates.NoPreviewOrDetailsAvailable;
				}
			}
		}

		public void UpdateDateDisplay()
		{
			SelectedItem?.FileDetails?.ForEach(property =>
			{
				if (property.Value is DateTimeOffset)
					property.UpdateValueText();
			});
		}

		[DynamicWindowsRuntimeCast(typeof(Frame))]
		private async void PreviewSettingsService_OnPropertyChangedEvent(object? sender, PropertyChangedEventArgs e)
		{
			try
			{
				if (e.PropertyName is nameof(infoPaneSettingsService.SelectedTab))
				{
					OnPropertyChanged(nameof(SelectedTab));

					// The preview will need refreshing as the file details won't be accurate
					var shouldUpdatePreview = ((MainWindow.Instance.Content as Frame)?.Content as MainPage)?.ViewModel.ShouldPreviewPaneBeActive;
					if (shouldUpdatePreview == true)
						await UpdateSelectedItemPreviewAsync();
				}
				else if (e.PropertyName is nameof(infoPaneSettingsService.IsInfoPaneEnabled))
				{
					var newEnablingStatus = infoPaneSettingsService.IsInfoPaneEnabled;
					if (isEnabled != newEnablingStatus)
					{
						isEnabled = newEnablingStatus;
						if (!isEnabled)
							UnloadPreview();

						OnPropertyChanged(nameof(IsEnabled));
					}
				}
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, ex.Message);
			}
		}

		private async Task LoadBasicPreviewAsync(CancellationToken token)
		{
			try
			{
				if (SelectedItem is not { } selectedItem)
					throw new InvalidOperationException("The preview pane does not have a selected item.");

				var basicModel = new BasicPreviewViewModel(selectedItem);
				PreviewPaneContent = new BasicPreview(basicModel);
				PreviewPaneState = SelectedDriveItem is not null ? PreviewPaneStates.DriveStorageDetailsAvailable : PreviewPaneStates.PreviewAndDetailsAvailable;

				await basicModel.LoadAsync();

				if (token.IsCancellationRequested)
					return;
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}
		}

		private CancellationToken CreatePreviewLoadToken()
		{
			CancelPreviewLoad();
			loadCancellationTokenSource = new CancellationTokenSource();

			return loadCancellationTokenSource.Token;
		}

		private void CancelPreviewLoad()
		{
			var cancellationTokenSource = loadCancellationTokenSource;
			loadCancellationTokenSource = null;

			cancellationTokenSource?.Cancel();
			cancellationTokenSource?.Dispose();
		}

		public void UnloadPreview()
		{
			previewRequestVersion++;
			CancelPreviewLoad();

			// Release the native preview host before clearing its view.
			if (previewPaneContent is ShellPreview shellPreview)
				shellPreview.UnloadPreview();

			PreviewPaneContent = null;
		}

		private void SelectedItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is nameof(ListedItem.HasTags))
				OnPropertyChanged(nameof(LoadTagsList));
			else if (e.PropertyName is nameof(ListedItem.FileTagsUI))
				UpdateTagsItems();
		}

		private void UpdateTagsItems()
		{
			try
			{
				Items.Clear();
				var selectedItem = SelectedItem;
				selectedItem?.FileTagsUI?.ForEach(tag => Items.Add(new TagItem(tag)));
				ListedItem[] selectedItems = selectedItem is null ? [] : [selectedItem];

				// Create menu once and reuse it for subsequent selections
				if (cachedTagsContextMenu is null)
				{
					cachedTagsContextMenu = new Files.App.UserControls.Menus.FileTagsContextMenu(selectedItems);
					cachedTagsContextMenu.TagsChanged += async (s, e) =>
					{
						if (contentPageContext.ShellPage is { } shellPage)
							await shellPage.GetRequiredShellViewModel().RefreshTagGroups();
					};
				}
				else
				{
					// Reset menu for new selection
					cachedTagsContextMenu.ResetForItems(selectedItems);
				}

				Items.Add(new FlyoutItem(cachedTagsContextMenu));
			}
			catch (COMException ex)
			{
				// MenuFlyout construction fails (0x8001010E: RPC_E_DISCONNECTED) when UI is being torn down
				App.Logger.LogInformation($"InfoPaneViewModel.UpdateTagsItems: Suppressed COMException during UI teardown: {ex.HResult}");
				Items.Clear(); // Clean up partial state
			}
		}

		private void SetDriveItem()
		{
			if (selectedItem is null)
			{
				selectedDriveItem = null;
				return;
			}

			var normalizedPath = PathNormalization.NormalizePath(selectedItem.ItemPath!);

			SelectedDriveItem = drivesViewModel.Drives
				.OfType<DriveItem>()
				.Concat(CloudDrivesManager.Drives)
				.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.Path) &&
					normalizedPath.Equals(PathNormalization.NormalizePath(d.Path), StringComparison.OrdinalIgnoreCase));
		}

		public void Dispose()
		{
			UnloadPreview();
			infoPaneSettingsService.PropertyChanged -= PreviewSettingsService_OnPropertyChangedEvent;
			contentPageContext.PropertyChanged -= ContentPageContext_PropertyChanged;
			CloudDrivesManager.DataChanged -= CloudDrivesManager_DataChanged;
			cachedTagsContextMenu = null;
		}
	}
}
