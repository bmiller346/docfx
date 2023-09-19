// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Specialized;
using System.ComponentModel;

using Newtonsoft.Json;

namespace Docfx.Plugins;

public class Manifest
{
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly Dictionary<string, List<OutputFileInfo>> _index = new();

    public Manifest()
    {
        Files = new ManifestItemCollection();
        Files.CollectionChanged += FileCollectionChanged;
    }

    public Manifest(IEnumerable<ManifestItem> files)
        : this()
    {
        Files.AddRange(files);
    }

    [JsonProperty("sitemap")]
    public SitemapOptions SitemapOptions { get; set; }

    [JsonProperty("source_base_path")]
    public string SourceBasePath { get; set; }

    [Obsolete]
    [JsonProperty("xrefmap")]
    public object XRefMap { get; set; }

    [JsonProperty("files")]
    public ManifestItemCollection Files { get; }

    [JsonProperty("groups")]
    public List<ManifestGroupInfo> Groups { get; set; }

    #region Public Methods

    public OutputFileInfo FindOutputFileInfo(string relativePath)
    {
        List<OutputFileInfo> list;
        _lock.EnterReadLock();
        try
        {
            _index.TryGetValue(NormalizePath(relativePath), out list);
        }
        finally
        {
            _lock.ExitReadLock();
        }
        if (list?.Count > 0)
        {
            return list[0];
        }
        return null;
    }

    #endregion

    #region EventHandlers

    private void FileCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        _lock.EnterWriteLock();
        try
        {
            if (e.NewItems != null)
            {
                foreach (ManifestItem item in e.NewItems)
                {
                    foreach (var ofi in item.OutputFiles.Values)
                    {
                        AddItem(ofi.RelativePath, ofi);
                        ofi.PropertyChanged += OutputFileInfoPropertyChanged;
                    }
                    item.OutputFiles.CollectionChanged += ManifestItemOutputChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (ManifestItem item in e.OldItems)
                {
                    foreach (var ofi in item.OutputFiles.Values)
                    {
                        RemoveItem(ofi.RelativePath, ofi);
                        ofi.PropertyChanged -= OutputFileInfoPropertyChanged;
                    }
                    item.OutputFiles.CollectionChanged -= ManifestItemOutputChanged;
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void ManifestItemOutputChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        _lock.EnterWriteLock();
        try
        {
            if (e.NewItems != null)
            {
                foreach (KeyValuePair<string, OutputFileInfo> item in e.NewItems)
                {
                    AddItem(item.Value.RelativePath, item.Value);
                    item.Value.PropertyChanged += OutputFileInfoPropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (KeyValuePair<string, OutputFileInfo> item in e.OldItems)
                {
                    RemoveItem(item.Value.RelativePath, item.Value);
                    item.Value.PropertyChanged -= OutputFileInfoPropertyChanged;
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void OutputFileInfoPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e is PropertyChangedEventArgs<string> args)
        {
            if (args.PropertyName != nameof(OutputFileInfo.RelativePath))
            {
                return;
            }
            _lock.EnterWriteLock();
            try
            {
                RemoveItem(args.Original, (OutputFileInfo)sender);
                AddItem(args.Current, (OutputFileInfo)sender);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }

    #endregion

    #region Private Methods

    private void AddItem(string relativePath, OutputFileInfo item)
    {
        var rp = NormalizePath(relativePath);
        if (_index.TryGetValue(rp, out List<OutputFileInfo> list))
        {
            list.Add(item);
        }
        else
        {
            _index[rp] = new List<OutputFileInfo> { item };
        }
    }

    private void RemoveItem(string relativePath, OutputFileInfo item)
    {
        if (_index.TryGetValue(NormalizePath(relativePath), out List<OutputFileInfo> list))
        {
            list.Remove(item);
        }
    }

    private static string NormalizePath(string relativePath)
    {
        return relativePath.Replace('\\', '/');
    }

    #endregion
}
