// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Previews;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.UI.Text;

namespace Files.App.UserControls.FilePreviews
{
	public sealed partial class DocxPreview : UserControl
	{
		public DocxPreview(DocxPreviewViewModel viewModel)
		{
			ViewModel = viewModel;
			InitializeComponent();
			ViewModel.PropertyChanged += ViewModel_PropertyChanged;
			BuildBlocks();
		}

		public DocxPreviewViewModel ViewModel { get; set; }

		private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(DocxPreviewViewModel.Paragraphs))
				BuildBlocks();
		}

		private void BuildBlocks()
		{
			DocumentText.Blocks.Clear();

			foreach (var paragraph in ViewModel.Paragraphs)
				DocumentText.Blocks.Add(ToRichParagraph(paragraph));
		}

		private static Paragraph ToRichParagraph(DocxParagraph paragraph)
		{
			var block = new Paragraph();

			switch (paragraph.HeadingLevel)
			{
				case 1:
					block.FontSize = 24;
					block.FontWeight = FontWeights.SemiBold;
					break;
				case 2:
					block.FontSize = 20;
					block.FontWeight = FontWeights.SemiBold;
					break;
				case 3:
					block.FontSize = 17;
					block.FontWeight = FontWeights.SemiBold;
					break;
				case > 3:
					block.FontSize = 15;
					block.FontWeight = FontWeights.SemiBold;
					break;
			}

			if (paragraph.HeadingLevel > 0)
				block.Margin = new Thickness(0, 10, 0, 4);

			if (paragraph.IsListItem)
				block.Inlines.Add(new Run { Text = "• " });

			foreach (var run in paragraph.Runs)
			{
				block.Inlines.Add(new Run
				{
					Text = run.Text,
					FontWeight = run.Bold ? FontWeights.Bold : FontWeights.Normal,
					FontStyle = run.Italic ? FontStyle.Italic : FontStyle.Normal,
				});
			}

			return block;
		}
	}
}
