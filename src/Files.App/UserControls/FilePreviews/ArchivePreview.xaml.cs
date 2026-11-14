// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Previews;
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
	}
}
