// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Helpers.Associations
{
	/// <summary>
	/// Read-only row for the associations preview dialog (Folder / Win+E / archive types).
	/// </summary>
	public sealed class AssociationsPreviewItem
	{
		public required string Title { get; init; }

		public required string CurrentHandler { get; init; }

		public string? Detail { get; init; }
	}
}
