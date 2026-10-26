using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BUGrockLauncher.WPFDataTypes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BUGrockLauncher {
    public class VersionList : ObservableCollection<WPFDataTypes.Version> {

        public string VersionsApiUWP { get; set; }
        public string VersionsApiGDK { get; set; }

        private static readonly string GDK_CONFIG_FILENAME = "MicrosoftGame.Config";

        private readonly string _cacheFileUWP;
        private readonly string _cacheFileGDK;
        private readonly string _importedDirectory;
        private readonly ICommonVersionCommands _commands;
        private readonly HttpClient _client = new HttpClient();
        private readonly HashSet<string> _dbVersions = new HashSet<string>();
        private readonly PropertyChangedEventHandler _versionPropertyChangedHandler;

        public VersionList(string cacheFileUWP, string importedDirectory, string versionsApiUWP, ICommonVersionCommands commands, PropertyChangedEventHandler versionPropertyChangedEventHandler, string cacheFileGDK, string versionsApiGDK) {
            _cacheFileUWP = cacheFileUWP;
            _cacheFileGDK = cacheFileGDK;
            _importedDirectory = importedDirectory;
            VersionsApiUWP = versionsApiUWP;
            VersionsApiGDK = versionsApiGDK;
            _commands = commands;
            _versionPropertyChangedHandler = versionPropertyChangedEventHandler;
            CollectionChanged += OnCollectionChanged;
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            if (e.Action == NotifyCollectionChangedAction.Reset) {
                foreach (var item in this) {
                    item.PropertyChanged -= _versionPropertyChangedHandler;
                    item.PropertyChanged += _versionPropertyChangedHandler;
                }
                return;
            }
            if (e.OldItems != null) {
                foreach (var item in e.OldItems) {
                    if (item is WPFDataTypes.Version version) {
                        version.PropertyChanged -= _versionPropertyChangedHandler;
                    }
                }
            }
            if (e.NewItems != null) {
                foreach (var item in e.NewItems) {
                    if (item is WPFDataTypes.Version version) {
                        version.PropertyChanged += _versionPropertyChangedHandler;
                    }
                }
            }
        }

        public void AddRange(IEnumerable<WPFDataTypes.Version> items) {
            CheckReentrancy();
            foreach (var item in items) {
                item.PropertyChanged += _versionPropertyChangedHandler;
                Items.Add(item);
            }
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        private void ParseListUWP(JArray? data, bool isCache) {
            if (data == null) return;
            var list = new List<WPFDataTypes.Version>();
            // ([name, uuid, versionType])[]
            foreach (JArray o in data.AsEnumerable().Reverse()) {
                if (o.Count < 3) continue;
                string? name = o[0]?.Value<string>();
                string? uuid = o[1]?.Value<string>();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(uuid)) continue;
                bool exists = !_dbVersions.Add(name);
                bool isNew = !exists && !isCache;
                int versionType = o[2].Value<int>();
                if (!Enum.IsDefined(typeof(VersionType), versionType) || versionType == (int)VersionType.Imported)
                    continue;
                list.Add(new WPFDataTypes.Version(uuid, name, (VersionType)versionType, isNew, _commands, PackageType.UWP, null));
            }
            AddRange(list);
        }

        private void ParseListGDK(JObject? data, bool isCache, VersionType versionType) {
            if (data == null) return;
            var list = new List<WPFDataTypes.Version>();
            foreach (var keyValue in data.Properties().Reverse()) {
                string versionName = keyValue.Name;
                if (keyValue.Value is not JArray urls) continue;

                List<string> downloadUrls = new List<string>();
                foreach (var url in urls) {
                    string? urlStr = url?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(urlStr)) {
                        downloadUrls.Add(urlStr);
                    }
                }
                if (downloadUrls.Count == 0) continue;
                bool exists = !_dbVersions.Add(versionName);
                bool isNew = !exists && !isCache;

                list.Add(new WPFDataTypes.Version(WPFDataTypes.Version.UNKNOWN_UUID, versionName, versionType, isNew, _commands, PackageType.GDK, downloadUrls));
            }
            AddRange(list);
        }

        private void ParseDataGDK(JObject? data, bool isCache) {
            if (data == null) return;
            ParseListGDK(data["release"] as JObject, isCache, VersionType.Release);
            ParseListGDK(data["preview"] as JObject, isCache, VersionType.Preview);
        }

        public void PrepareForReload() {
            var imported = this.Where(v => v.VersionType == VersionType.Imported).ToList();
            _dbVersions.Clear();
            foreach (var imp in imported) {
                _dbVersions.Add("IMPORTED_" + Path.GetFileName(imp.GameDirectory));
            }
            Items.Clear();
            foreach (var imp in imported) {
                Items.Add(imp);
            }
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public async Task LoadImported() {
            string[] subdirectoryEntries = await Task.Run(() => Directory.Exists(_importedDirectory) ? Directory.GetDirectories(_importedDirectory) : Array.Empty<string>());
            foreach (string subdirectory in subdirectoryEntries) {
                if (_dbVersions.Add("IMPORTED_" + Path.GetFileName(subdirectory))) {
                    AddEntry(Path.GetFileName(subdirectory), subdirectory, File.Exists(Path.Combine(subdirectory, GDK_CONFIG_FILENAME)) ? PackageType.GDK : PackageType.UWP);
                }
            }
        }

        public async Task LoadFromCacheUWP() {
            try {
                string data;
                if (File.Exists(_cacheFileUWP)) {
                    data = await File.ReadAllTextAsync(_cacheFileUWP);
                } else {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                    using var stream = asm.GetManifestResourceStream("BUGrockLauncher.uwp_versions.json");
                    if (stream == null) return;
                    using var reader = new StreamReader(stream);
                    data = await reader.ReadToEndAsync();
                    try { File.WriteAllText(_cacheFileUWP, data); } catch { }
                }
                var parsed = await Task.Run(() => JArray.Parse(data));
                ParseListUWP(parsed, true);
            } catch (Exception ex) {
                Trace.WriteLine("Corrupted UWP cache file ignored: " + ex.Message);
            }
        }

        public async Task LoadFromCacheGDK() {
            try {
                if (!File.Exists(_cacheFileGDK)) return;
                string data = await File.ReadAllTextAsync(_cacheFileGDK);
                var parsed = await Task.Run(() => JObject.Parse(data));
                ParseDataGDK(parsed, true);
            } catch (Exception ex) {
                Trace.WriteLine("Corrupted GDK cache file ignored: " + ex.Message);
            }
        }

        public async Task DownloadVersionsGDK() {
            var resp = await _client.GetAsync(VersionsApiGDK);
            resp.EnsureSuccessStatusCode();
            var data = await resp.Content.ReadAsStringAsync();
            await Task.Run(() => {
                try { File.WriteAllText(_cacheFileGDK, data); } catch { }
            });
            var parsed = await Task.Run(() => JObject.Parse(data));
            ParseDataGDK(parsed, false);
        }

        public async Task DownloadVersionsUWP() {
            var resp = await _client.GetAsync(VersionsApiUWP);
            resp.EnsureSuccessStatusCode();
            var data = await resp.Content.ReadAsStringAsync();
            await Task.Run(() => {
                try { File.WriteAllText(_cacheFileUWP, data); } catch { }
            });
            var parsed = await Task.Run(() => JArray.Parse(data));
            ParseListUWP(parsed, false);
        }

        public WPFDataTypes.Version AddEntry(string name, string path, PackageType packageType) {
            string cleanName = name;
            if (cleanName.EndsWith(".appx", StringComparison.OrdinalIgnoreCase)) {
                cleanName = cleanName.Substring(0, cleanName.Length - 5);
            } else if (cleanName.EndsWith(".msixvc", StringComparison.OrdinalIgnoreCase)) {
                cleanName = cleanName.Substring(0, cleanName.Length - 7);
            }
            var result = new WPFDataTypes.Version(cleanName, path, _commands, packageType);
            Add(result);
            return result;
        }

    }
}
