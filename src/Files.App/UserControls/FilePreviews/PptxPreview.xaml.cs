// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Previews;
using Microsoft.UI.Xaml.Controls;

namespace Files.App.UserControls.FilePreviews
{
	public sealed partial class PptxPreview : UserControl
	{
		public PptxPreview(PptxPreviewViewModel viewModel)
		{
			ViewModel = viewModel;
			InitializeComponent();
		}

		public PptxPreviewViewModel ViewModel { get; set; }
	}
}
