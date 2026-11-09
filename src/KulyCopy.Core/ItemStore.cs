using Microsoft.Data.Sqlite;

namespace KulyCopy.Core;

/// <summary>
/// Persistência do histórico em SQLite. Todas as operações são síncronas e protegidas por lock,
/// pois o app é single-process e a UI roda em uma thread só.
/// </summary>
public sealed class ItemStore : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly object _lock = new();

    public string Path { get; }

    public ItemStore(string path)
    {
        Path = path;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();

        _conn = new SqliteConnection(cs);
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL");
        Exec("PRAGMA synchronous=NORMAL");
        Exec("PRAGMA foreign_keys=ON");
        CreateSchema();
    }

    private void CreateSchema()
    {
        Exec(
            "CREATE TABLE IF NOT EXISTS tabs (" +
            " id INTEGER PRIMARY KEY AUTOINCREMENT," +
            " name TEXT NOT NULL UNIQUE," +
            " position INTEGER NOT NULL DEFAULT 0," +
            " icon TEXT," +
            " max_items INTEGER);" +
            "CREATE TABLE IF NOT EXISTS items (" +
            " id INTEGER PRIMARY KEY AUTOINCREMENT," +
            " tab_id INTEGER NOT NULL REFERENCES tabs(id) ON DELETE CASCADE," +
            " position REAL NOT NULL," +
            " created INTEGER NOT NULL," +
            " hash TEXT NOT NULL," +
            " pinned INTEGER NOT NULL DEFAULT 0," +
            " notes TEXT);" +
            "CREATE INDEX IF NOT EXISTS idx_items_tab_pos ON items(tab_id, position);" +
            "CREATE INDEX IF NOT EXISTS idx_items_tab_hash ON items(tab_id, hash);" +
            "CREATE TABLE IF NOT EXISTS item_data (" +
            " item_id INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE," +
            " mime TEXT NOT NULL," +
            " data BLOB NOT NULL," +
            " PRIMARY KEY (item_id, mime));" +
            "CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);");
        if (GetMeta("schema_version") is null) SetMeta("schema_version", "1");
    }

    // ---------- Helpers ----------

    private SqliteCommand Cmd(string sql, params (string name, object? value)[] args)
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private int Exec(string sql, params (string, object?)[] args)
    {
        lock (_lock)
        {
            using var cmd = Cmd(sql, args);
            return cmd.ExecuteNonQuery();
        }
    }

    private object? Scalar(string sql, params (string, object?)[] args)
    {
        lock (_lock)
        {
            using var cmd = Cmd(sql, args);
            return cmd.ExecuteScalar();
        }
    }

    // ---------- Meta ----------

    public string? GetMeta(string key) =>
        Scalar("SELECT value FROM meta WHERE key=$k", ("$k", key)) as string;

    public void SetMeta(string key, string value) =>
        Exec("INSERT INTO meta(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
            ("$k", key), ("$v", value));

    // ---------- Tabs ----------

    public List<TabRecord> GetTabs()
    {
        lock (_lock)
        {
            var list = new List<TabRecord>();
            using var cmd = Cmd("SELECT id, name, position, icon, max_items FROM tabs ORDER BY position, id");
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new TabRecord
                {
                    Id = r.GetInt64(0),
                    Name = r.GetString(1),
                    Position = r.GetInt32(2),
                    Icon = r.IsDBNull(3) ? null : r.GetString(3),
                    MaxItems = r.IsDBNull(4) ? null : r.GetInt32(4),
                });
            }
            return list;
        }
    }

    public TabRecord CreateTab(string name)
    {
        lock (_lock)
        {
            var pos = Convert.ToInt32(Scalar("SELECT COALESCE(MAX(position), -1) + 1 FROM tabs"));
            Exec("INSERT INTO tabs(name, position) VALUES($n, $p)", ("$n", name), ("$p", pos));
            var id = Convert.ToInt64(Scalar("SELECT last_insert_rowid()"));
            return new TabRecord { Id = id, Name = name, Position = pos };
        }
    }

    public void RenameTab(long id, string name) =>
        Exec("UPDATE tabs SET name=$n WHERE id=$id", ("$n", name), ("$id", id));

    public void SetTabIcon(long id, string? icon) =>
        Exec("UPDATE tabs SET icon=$i WHERE id=$id", ("$i", icon), ("$id", id));

    public void SetTabMaxItems(long id, int? max) =>
        Exec("UPDATE tabs SET max_items=$m WHERE id=$id", ("$m", max), ("$id", id));

    public void RemoveTab(long id) =>
        Exec("DELETE FROM tabs WHERE id=$id", ("$id", id));

    public void SetTabOrder(IReadOnlyList<long> ids)
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            for (var i = 0; i < ids.Count; i++)
            {
                using var cmd = Cmd("UPDATE tabs SET position=$p WHERE id=$id", ("$p", i), ("$id", ids[i]));
                cmd.Transaction = tx;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    // ---------- Items ----------

    public List<ItemRecord> LoadItems(long tabId)
    {
        lock (_lock)
        {
            var items = new List<ItemRecord>();
            var byId = new Dictionary<long, ItemRecord>();
            using (var cmd = Cmd("SELECT id, position, created, hash, pinned, notes FROM items WHERE tab_id=$t ORDER BY position, id", ("$t", tabId)))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var rec = new ItemRecord
                    {
                        Id = r.GetInt64(0),
                        TabId = tabId,
                        Position = r.GetDouble(1),
                        Created = DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(2)).ToLocalTime(),
                        Hash = r.GetString(3),
                        Pinned = r.GetInt64(4) != 0,
                        Notes = r.IsDBNull(5) ? null : r.GetString(5),
                    };
                    items.Add(rec);
                    byId[rec.Id] = rec;
                }
            }

            using (var cmd = Cmd("SELECT d.item_id, d.mime, d.data FROM item_data d JOIN items i ON i.id = d.item_id WHERE i.tab_id=$t", ("$t", tabId)))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var id = r.GetInt64(0);
                    if (!byId.TryGetValue(id, out var rec)) continue;
                    rec.Data[r.GetString(1)] = (byte[])r[2];
                }
            }
            return items;
        }
    }

    public ItemRecord InsertItem(long tabId, double position, IReadOnlyDictionary<string, byte[]> data,
        string hash, bool pinned, string? notes, DateTimeOffset created)
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            long id;
            using (var cmd = Cmd(
                "INSERT INTO items(tab_id, position, created, hash, pinned, notes) VALUES($t, $p, $c, $h, $pin, $n); SELECT last_insert_rowid();",
                ("$t", tabId), ("$p", position), ("$c", created.ToUnixTimeMilliseconds()), ("$h", hash),
                ("$pin", pinned ? 1 : 0), ("$n", notes)))
            {
                cmd.Transaction = tx;
                id = Convert.ToInt64(cmd.ExecuteScalar());
            }
            WriteData(tx, id, data);
            tx.Commit();

            return new ItemRecord
            {
                Id = id, TabId = tabId, Position = position, Created = created, Hash = hash,
                Pinned = pinned, Notes = notes,
                Data = data.ToDictionary(k => k.Key, v => v.Value),
            };
        }
    }

    private void WriteData(SqliteTransaction tx, long itemId, IReadOnlyDictionary<string, byte[]> data)
    {
        foreach (var kv in data)
        {
            using var cmd = Cmd("INSERT OR REPLACE INTO item_data(item_id, mime, data) VALUES($i, $m, $d)",
                ("$i", itemId), ("$m", kv.Key), ("$d", kv.Value));
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
    }

    public void ReplaceData(long itemId, IReadOnlyDictionary<string, byte[]> data, string hash)
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            using (var del = Cmd("DELETE FROM item_data WHERE item_id=$i", ("$i", itemId)))
            {
                del.Transaction = tx;
                del.ExecuteNonQuery();
            }
            WriteData(tx, itemId, data);
            using (var up = Cmd("UPDATE items SET hash=$h WHERE id=$i", ("$h", hash), ("$i", itemId)))
            {
                up.Transaction = tx;
                up.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public void SetPosition(long itemId, double position) =>
        Exec("UPDATE items SET position=$p WHERE id=$i", ("$p", position), ("$i", itemId));

    public void SetPositions(IEnumerable<(long id, double position)> positions)
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            foreach (var (id, pos) in positions)
            {
                using var cmd = Cmd("UPDATE items SET position=$p WHERE id=$i", ("$p", pos), ("$i", id));
                cmd.Transaction = tx;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public void SetPinned(long itemId, bool pinned) =>
        Exec("UPDATE items SET pinned=$p WHERE id=$i", ("$p", pinned ? 1 : 0), ("$i", itemId));

    public void SetNotes(long itemId, string? notes) =>
        Exec("UPDATE items SET notes=$n WHERE id=$i", ("$n", notes), ("$i", itemId));

    public void SetCreated(long itemId, DateTimeOffset created) =>
        Exec("UPDATE items SET created=$c WHERE id=$i", ("$c", created.ToUnixTimeMilliseconds()), ("$i", itemId));

    public void MoveToTab(long itemId, long tabId, double position) =>
        Exec("UPDATE items SET tab_id=$t, position=$p WHERE id=$i", ("$t", tabId), ("$p", position), ("$i", itemId));

    public void RemoveItems(IEnumerable<long> ids)
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            foreach (var id in ids)
            {
                using var cmd = Cmd("DELETE FROM items WHERE id=$i", ("$i", id));
                cmd.Transaction = tx;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public int CountItems(long tabId) =>
        Convert.ToInt32(Scalar("SELECT COUNT(*) FROM items WHERE tab_id=$t", ("$t", tabId)));

    public void Vacuum() => Exec("VACUUM");

    public void Dispose()
    {
        lock (_lock)
        {
            _conn.Dispose();
        }
    }
}
