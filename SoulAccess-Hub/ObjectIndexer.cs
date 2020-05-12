using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SoulAccess.Hub {
    public class ObjectIndexerConfig {
        public string StorageDirPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "soulaccess/storage");
    }

    public class ObjectIndex {
        // Name of the file. Also the identifier of the file.
        public string Name { get; set; }
        // The time when the latest modificaiton was done.
        public DateTime LastModifiedUtc { get; set; }
        // The physical size of the file.
        public long FileSize { get; set; }

        public ObjectIndex(FileInfo fi) {
            Name = fi.Name;
            LastModifiedUtc = fi.LastWriteTimeUtc;
            FileSize = fi.Length;
        }
    }

    public class ObjectIndexer : IEnumerable<ObjectIndex> {
        // Used to synchronize accesses.
        private object _SyncRoot;
        private readonly ObjectIndexerConfig _Cfg;
        // Index is sorted by the last modification time.
        private List<ObjectIndex> _Idxs;
        private Dictionary<string, ObjectIndex> _NameMap;
        private Comparer<ObjectIndex> _LastModCmp;

        public ObjectIndexer() {
            _Cfg = new ObjectIndexerConfig();
            _Idxs = new List<ObjectIndex>();
            _SyncRoot = new object();
            _LastModCmp = Comparer<ObjectIndex>.Create((a, b) => {
                return -a.LastModifiedUtc.CompareTo(b.LastModifiedUtc);
            });
            if (!Directory.Exists(_Cfg.StorageDirPath)) {
                Directory.CreateDirectory(_Cfg.StorageDirPath);
            }
            IndexAll();
        }

        // Get the index of a file identified by `name`.
        //
        // Returns false if the file hasn't been indexed.
        public bool TryGetIndex(string name, out ObjectIndex idx) {
            return _NameMap.TryGetValue(name, out idx);
        }
        // Index all local files in the storage directory. The indexed data will
        // be kept if the call failed.
        public bool IndexAll() {
            lock (_SyncRoot) {
                IEnumerable<ObjectIndex> idxs;
                try {
                    idxs = from x in Directory.EnumerateFiles(_Cfg.StorageDirPath)
                           select new ObjectIndex(new FileInfo(Path.Combine(_Cfg.StorageDirPath, x)));
                } catch (Exception) {
                    return false;
                }
                _Idxs.Clear();
                _Idxs.AddRange(idxs);
                _Idxs.Sort(_LastModCmp);
                _NameMap = _Idxs.ToDictionary(x => x.Name);
                return true;
            }
        }
        // Attempt to add a file identified by `name` to the object. The file
        // MUST exists in the file system before being added.
        //
        // Returns false if the file doesn't exists.
        public bool Add(string name) {
            var path = Path.Combine(_Cfg.StorageDirPath, name);
            lock (_SyncRoot) {
                if (!File.Exists(path)) { return false; }
                FileInfo fi;
                try {
                    fi = new FileInfo(path);
                } catch (Exception) {
                    return false;
                }
                var idx = new ObjectIndex(fi);
                var i = _Idxs.BinarySearch(idx, _LastModCmp);
                _Idxs.Insert(i < 0 ? ~i : i, idx);
                _NameMap.Add(name, idx);
                return true;
            }
        }
        // Remove a file from the index, the corresponding physical file will
        // also be removed from local storage. A file will not be removed if it
        // it's not indexed.
        //
        // Return `false` if the file doesn't exists beforehand; otherwise,
        // `true` is returned.
        public bool Remove(string name) {
            var path = Path.Combine(_Cfg.StorageDirPath, name);
            lock (_SyncRoot) {
                if (_NameMap.TryGetValue(name, out var idx)) {
                    _NameMap.Remove(name);
                    _Idxs.Remove(idx);
                    try {
                        if (File.Exists(name)) { File.Delete(path); }
                    } catch (Exception) {
                        // Ignore deletion error.
                    }
                    return true;
                } else {
                    return false;
                }
            }
        }
        // Open a stream to an existing file.
        //
        // Returns false if the file doesn't exists.
        public bool OpenRead(string name, out FileStream fs) {
            var path = Path.Combine(_Cfg.StorageDirPath, name);
            lock (_SyncRoot) {
                if (!File.Exists(path)) { goto fail; }
                try {
                    fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 4096, useAsync: true);
                    return true;
                } catch (Exception) { goto fail; }
            }
        fail:
            fs = null;
            return false;
        }
        // Create a stream to a newly created file. The created file WILL NOT be
        // automatically added to.
        //
        // Returns false if the file already exists.
        public bool OpenWrite(string name, out FileStream fs) {
            var path = Path.Combine(_Cfg.StorageDirPath, name);
            lock (_SyncRoot) {
                if (File.Exists(path)) { goto fail; }
                try {
                    fs = new FileStream(path, FileMode.Create, FileAccess.Write,
                        FileShare.None, 4096, useAsync: true);
                    return true;
                } catch (Exception) { goto fail; }
            }
        fail:
            fs = null;
            return false;
        }

        public IEnumerator<ObjectIndex> GetEnumerator() {
            return ((IEnumerable<ObjectIndex>)_Idxs).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return ((IEnumerable<ObjectIndex>)_Idxs).GetEnumerator();
        }
    }
}
