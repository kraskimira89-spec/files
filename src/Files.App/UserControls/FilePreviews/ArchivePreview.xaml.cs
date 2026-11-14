// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Previews;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.App.UserControls.FilePreviews
{
	public sealed partial class ArchivePreview : UserControl
	{
		public ArchivePreview(ArchivePreviewViewModel viewModel)
		{
			ViewModel = viewModel;
			InitializeComponent();
		}

		public ArchivePreviewViewModel ViewModel { get; set; }

		private void ArchiveTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
		{
			ViewModel.SelectedEntry = args.AddedItems.Count > 0 ? args.AddedItems[0] as ArchiveNode : null;
		}

		private async void ExtractEntry_Click(object sender, RoutedEventArgs e)
		{
			await ViewModel.ExtractSelectedAsync();
		}
	}
}
