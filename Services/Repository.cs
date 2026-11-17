using System;
using System.Collections.Generic;
using System.Text;
using AdvancedClipboard.Models;
using Microsoft.Data.Sqlite;

namespace AdvancedClipboard.Services;

/// <summary>SQLite persistence. Text/HTML are stored as (optionally AES-GCM encrypted) blobs.</summary>
public sealed class Repository
{
    private readonly string _conn;

    public Repository()
    {
        AppPaths.EnsureDirs();
        _conn = new SqliteConnectionStringBuilder { DataSource = AppPaths.DbPath }.ToString();
        using var c = Open();
        Exec(c, @"CREATE TABLE IF NOT EXISTS items(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            type INTEGER NOT NULL,
            text BLOB, html BLOB, image_file TEXT, source_app TEXT,
            hash TEXT NOT NULL, size INTEGER NOT NULL DEFAULT 0,
            sensitive INTEGER NOT NULL DEFAULT 0,
            expires TEXT, created TEXT NOT NULL,
            pinned INTEGER NOT NULL DEFAULT 0, collection TEXT);");
        Exec(c, "CREATE INDEX IF NOT EXISTS ix_hash ON items(hash);");
        Exec(c, "CREATE INDEX IF NOT EXISTS ix_created ON items(created);");
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_conn);
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static byte[]? Enc(string? s) =>
        s == null ? null : Crypto.Protect(Encoding.UTF8.GetBytes(s), SettingsService.Current.EncryptHistory);

    private static string? Dec(object v)
    {
        if (v is not byte[] b) return null;
        var plain = Crypto.Unprotect(b);
        return plain == null ? null : Encoding.UTF8.GetString(plain);
    }

    private static string Iso(DateTime d) => d.ToUniversalTime().ToString("o");
    private static DateTime Parse(string s) => DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();

    public List<ClipItem> LoadAll()
    {
        var list = new List<ClipItem>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT id,type,text,html,image_file,source_app,hash,size,sensitive,expires,created,pinned,collection
                            FROM items ORDER BY created DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ClipItem
            {
                Id = r.GetInt64(0),
                Type = (ClipType)r.GetInt32(1),
                Text = r.IsDBNull(2) ? null : Dec(r.GetValue(2)),
                Html = r.IsDBNull(3) ? null : Dec(r.GetValue(3)),
                ImageFile = r.IsDBNull(4) ? null : r.GetString(4),
                SourceApp = r.IsDBNull(5) ? null : r.GetString(5),
                Hash = r.GetString(6),
                SizeBytes = r.GetInt64(7),
                Sensitive = r.GetInt32(8) != 0,
                ExpiresUtc = r.IsDBNull(9) ? null : Parse(r.GetString(9)),
                CreatedUtc = Parse(r.GetString(10)),
                Pinned = r.GetInt32(11) != 0,
                Collection = r.IsDBNull(12) ? null : r.GetString(12),
            });
        }
        return list;
    }

    public void Insert(ClipItem item)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO items(type,text,html,image_file,source_app,hash,size,sensitive,expires,created,pinned,collection)
                            VALUES($t,$x,$h,$i,$s,$hash,$sz,$sen,$exp,$cr,$p,$col); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$t", (int)item.Type);
        cmd.Parameters.AddWithValue("$x", (object?)Enc(item.Text) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$h", (object?)Enc(item.Html) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$i", (object?)item.ImageFile ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", (object?)item.SourceApp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hash", item.Hash);
        cmd.Parameters.AddWithValue("$sz", item.SizeBytes);
        cmd.Parameters.AddWithValue("$sen", item.Sensitive ? 1 : 0);
        cmd.Parameters.AddWithValue("$exp", item.ExpiresUtc.HasValue ? (object)Iso(item.ExpiresUtc.Value) : DBNull.Value);
        cmd.Parameters.AddWithValue("$cr", Iso(item.CreatedUtc));
        cmd.Parameters.AddWithValue("$p", item.Pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$col", (object?)item.Collection ?? DBNull.Value);
        item.Id = Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>Updates mutable fields (text, pin state, collection, created time, sensitivity, expiry).</summary>
    public void Update(ClipItem item)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"UPDATE items SET type=$t,text=$x,html=$h,hash=$hash,size=$sz,sensitive=$sen,
                            expires=$exp,created=$cr,pinned=$p,collection=$col WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", item.Id);
        cmd.Parameters.AddWithValue("$t", (int)item.Type);
        cmd.Parameters.AddWithValue("$x", (object?)Enc(item.Text) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$h", (object?)Enc(item.Html) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hash", item.Hash);
        cmd.Parameters.AddWithValue("$sz", item.SizeBytes);
        cmd.Parameters.AddWithValue("$sen", item.Sensitive ? 1 : 0);
        cmd.Parameters.AddWithValue("$exp", item.ExpiresUtc.HasValue ? (object)Iso(item.ExpiresUtc.Value) : DBNull.Value);
        cmd.Parameters.AddWithValue("$cr", Iso(item.CreatedUtc));
        cmd.Parameters.AddWithValue("$p", item.Pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$col", (object?)item.Collection ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM items WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteAllUnpinned()
    {
        using var c = Open();
        Exec(c, "DELETE FROM items WHERE pinned=0");
    }
}
