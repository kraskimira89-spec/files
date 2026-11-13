// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace Files.App.Server;

internal static class IconCache
{
	private static readonly ConcurrentDictionary<(string Extension, bool Folder, int Size), CachedTask<byte[]>> generic = new();

	private static readonly ConcurrentDictionary<(string Path, int Size, bool Folder, uint Options, bool Overlay), CachedTask<byte[]>> fileIcons = new();

	internal static async Task<IBuffer> GetGenericAsync(string extension, bool folder, int size)
	{
		ValidateSize(size);
		if (generic.Count > 512)
			generic.Clear();
		var key = (extension.ToLowerInvariant(), folder, size);
		var cache = generic.GetOrAdd(key, _ => new CachedTask<byte[]>(async () =>
		{
			var path = folder ? Environment.SystemDirectory : Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "x46696c6573") + extension;
			return await STATask.RunPooled(() => ShellImage.GetIcon(path, size, folder, IconOptions.ReturnIconOnly), Program.Logger) ?? [];
		}, TimeSpan.FromMinutes(5)));
		return (await cache.GetAsync()).AsBuffer();
	}

	internal static async Task<IBuffer> GetAsync(string path, int size, bool folder, uint options, bool overlay)
	{
		ValidateSize(size);
		if (fileIcons.Count > 1024)
			fileIcons.Clear();

		var key = (path, size, folder, options, overlay);
		var cache = fileIcons.GetOrAdd(key, _ =>
		{
			var monitor = new FileStampMonitor(() => (string[])[path]);
			return new CachedTask<byte[]>(
				async () =>
				{
					var resolved = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? MtpHelpers.ResolveMtpShellPath(path) ?? path : path;
					return await STATask.RunPooled(() =>
						overlay
							? ShellImage.GetIconOverlay(resolved, size, folder) ?? []
							: ShellImage.GetIcon(resolved, size, folder, (IconOptions)options) ?? [], Program.Logger);
				},
				TimeSpan.FromMinutes(2),
				hasChanged: monitor.HasChanged);
		});

		return (await cache.GetAsync()).AsBuffer();
	}

	private static void ValidateSize(int size)
	{
		if (size is < 1 or > 1024)
			throw new ArgumentOutOfRangeException(nameof(size));
	}
}
