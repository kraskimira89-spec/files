using Files.App.ViewModels.Previews;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Files.App.UserControls.FilePreviews
{
	public sealed partial class PDFPreview : UserControl
	{
		public PDFPreview(PDFPreviewViewModel model)
		{
			ViewModel = model;
			InitializeComponent();
			PageList.SelectionChanged += PageList_SelectionChanged;
			Unloaded += PDFPreview_Unloaded;
		}

		public PDFPreviewViewModel ViewModel { get; set; }

		private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (PageList.SelectedIndex >= 0)
				_ = ViewModel.RequestPageAsync(PageList.SelectedIndex);
		}

		private void PDFPreview_Unloaded(object sender, RoutedEventArgs e)
		{
			PageList.SelectionChanged -= PageList_SelectionChanged;
			Unloaded -= PDFPreview_Unloaded;
			ViewModel.PreviewControlBase_Unloaded(sender, e);
		}
	}
}