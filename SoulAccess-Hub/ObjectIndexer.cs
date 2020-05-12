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
            Update(fi);
        }

        public void Update(FileInfo fi) {
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
        // Index all local files in the storage directory. The indexed data will
        // be kept if the call failed.
        private bool IndexAll() {
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

        // Get the index of a file identified by `name`.
        //
        // Returns false if the file hasn't been indexed.
        public bool TryGetIndex(string name, out ObjectIndex idx) {
            return _NameMap.TryGetValue(name, out idx);
        }

        // Attempt to add a file identified by `name` to the object and allocate
        // space on local storage for that file.
        //
        public async Task<string> UpdateIndexAsync(string name) {
            var path = Path.Combine(_Cfg.StorageDirPath, name);
            return await Task.Run(() => {
                FileInfo fi;
                try {
                    fi = new FileInfo(path);
                } catch (Exception) {
                    fi = null;
                }
                var isAccessible = fi != null && fi.Exists;
                lock (_SyncRoot) {
                    if (!_NameMap.TryGetValue(name, out var idx)) {
                        // The object hasn't been indexed yet.
                        if (isAccessible) {
                            idx = new ObjectIndex(fi);
                            var i = _Idxs.BinarySearch(idx, _LastModCmp);
                            _Idxs.Insert(i < 0 ? ~i : i, idx);
                            _NameMap.Add(name, idx);
                        }
                    } else {
                        // The object is already indexed.
                        if (isAccessible) {
                            idx.Update(fi);
                        } else {
                            // Remove existing index if the file is not
                            // accessible.
                            RemoveIndex(name);
                            return "removed index of inaccessible object";
                        }
                    }
                }
                return null;
            });
        }

        // Remove a file from the index, the corresponding physical file will
        // also be removed from local storage. A file will not be removed if it
        // it's not indexed.
        public async Task RemoveAsync(string name) {
            var path = Path.Combine(_Cfg.StorageDirPath, name);
            await Task.Run(() => {
                lock (_SyncRoot) {
                    RemoveIndex(name);
                }
                try {
                    if (File.Exists(name)) { File.Delete(path); }
                } catch (Exception) {
                    // Ignore deletion error.
                }
            });
        }
        private void RemoveIndex(string name) {
            if (_NameMap.TryGetValue(name, out var idx)) {
                _NameMap.Remove(name);
                _Idxs.Remove(idx);
            }
        }

        public async Task<string> ReadAsync(string name, Stream dst) {
            if (TryOpenFile(name, true, out var src)) {
                using (src) {
                    await src.CopyToAsync(dst);
                }
                return null;
            } else {
                return "object is not accessible";
            }
        }
        // Asynchronously read a file to the `dst` stream from local file
        // identified by `name`, in range of [`from`, `to`).
        //
        // Returns `null` if the read succeeded; error message is returned
        // otherwise.
        public async Task<string> ReadAsync(string name, long from, long to, Stream dst) {
            if (from < 0) { return "out of range"; }
            if (TryOpenFile(name, true, out var src)) {
                using (src) {
                    if (to > src.Length) { return "out of range"; }
                    src.Seek(from, SeekOrigin.Begin);
                    await src.CopyToAsync(dst);
                }
                return null;
            } else {
                return "object is not accessible";
            }
        }
        // Asynchronously write the file identified by `name` with data in
        // `dst`, in range of [`from`, `to`).
        //
        // Returns `null` if the write succeeded; error message is returned
        // othersise.
        public async Task<string> WriteAsync(string name, long from, Stream src) {
            if (TryOpenFile(name, false, out var dst)) {
                using (dst) {
                    if (from > dst.Length) { return "out of range"; }
                    dst.Seek(from, SeekOrigin.Begin);
                    await src.CopyToAsync(dst);
                }
                return null;
            } else {
                return "object is not accessible";
            }
        }
        private bool TryOpenFile(string name, bool isRead, out FileStream fs) {
            var path = Path.Combine(_Cfg.StorageDirPath, name);
            (var mode, var access, var share) = isRead ?
                (FileMode.Open, FileAccess.Read, FileShare.Read) :
                (FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            try {
                fs = new FileStream(path, mode, access, share, 4096,
                    useAsync: true);
                return true;
            } catch (Exception) {
                fs = null;
                return false;
            }
        }

        public IEnumerator<ObjectIndex> GetEnumerator() {
            return ((IEnumerable<ObjectIndex>)_Idxs).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return ((IEnumerable<ObjectIndex>)_Idxs).GetEnumerator();
        }
    }
}
