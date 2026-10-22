using Microsoft.Data.Sqlite;
using Vox.Core.Text;

namespace Vox.Core.Storage;

public sealed record Transcription(
    long Id, string RawText, string ProcessedText, string Model, string Lang, long DurationMs, DateTimeOffset CreatedAt);

public sealed record DictionaryEntry(long Id, string Wrong, string Correct, string Lang, string Source);

/// <summary>
/// History, user dictionary and settings in vox_data.db. The schema is the one Vox 1.x created,
/// so an existing database opens as-is (see docs/csharp-migration.md §5); 2.0 only adds
/// correction_candidates, which 1.x ignores.
/// </summary>
public sealed class VoxDatabase : IDisposable, ICandidateStore
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS transcriptions (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            raw_text        TEXT    NOT NULL,
            processed_text  TEXT    NOT NULL,
            model           TEXT    NOT NULL DEFAULT '',
            lang            TEXT    NOT NULL DEFAULT '',
            duration_ms     INTEGER NOT NULL DEFAULT 0,
            app_context     TEXT    NOT NULL DEFAULT '',
            created_at      REAL    NOT NULL
        );
        CREATE VIRTUAL TABLE IF NOT EXISTS transcriptions_fts USING fts5(
            processed_text, content='transcriptions', content_rowid='id', tokenize='unicode61'
        );
        CREATE TRIGGER IF NOT EXISTS transcriptions_ai AFTER INSERT ON transcriptions BEGIN
            INSERT INTO transcriptions_fts(rowid, processed_text) VALUES (new.id, new.processed_text);
        END;
        CREATE TRIGGER IF NOT EXISTS transcriptions_ad AFTER DELETE ON transcriptions BEGIN
            INSERT INTO transcriptions_fts(transcriptions_fts, rowid, processed_text)
            VALUES ('delete', old.id, old.processed_text);
        END;
        CREATE TRIGGER IF NOT EXISTS transcriptions_au AFTER UPDATE ON transcriptions BEGIN
            INSERT INTO transcriptions_fts(transcriptions_fts, rowid, processed_text)
            VALUES ('delete', old.id, old.processed_text);
            INSERT INTO transcriptions_fts(rowid, processed_text) VALUES (new.id, new.processed_text);
        END;
        CREATE TABLE IF NOT EXISTS dictionary (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            wrong      TEXT    NOT NULL,
            correct    TEXT    NOT NULL,
            lang       TEXT    NOT NULL DEFAULT '',
            source     TEXT    NOT NULL DEFAULT 'manual',
            created_at REAL    NOT NULL,
            UNIQUE(wrong, lang)
        );
        CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS correction_candidates (
            wrong      TEXT PRIMARY KEY,
            correct    TEXT NOT NULL,
            created_at REAL NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_transcriptions_created ON transcriptions(created_at DESC);
        CREATE INDEX IF NOT EXISTS idx_transcriptions_model ON transcriptions(model);
        CREATE INDEX IF NOT EXISTS idx_dictionary_wrong ON dictionary(wrong);
        INSERT OR IGNORE INTO settings(key, value) VALUES ('db_version', '1');
        """;

    private readonly SqliteConnection _connection;
    private readonly Lock _lock = new();

    public VoxDatabase(string path)
    {
        SQLitePCL.Batteries_V2.Init();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connection = new SqliteConnection($"Data Source={path}");
        _connection.Open();
        Execute("PRAGMA journal_mode=WAL;");
        Execute(Schema);
    }

    public long AddTranscription(string raw, string processed, string model, string lang, long durationMs)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO transcriptions (raw_text, processed_text, model, lang, duration_ms, app_context, created_at)
                VALUES ($raw, $processed, $model, $lang, $duration, '', $created);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$raw", raw);
            cmd.Parameters.AddWithValue("$processed", processed);
            cmd.Parameters.AddWithValue("$model", model);
            cmd.Parameters.AddWithValue("$lang", lang);
            cmd.Parameters.AddWithValue("$duration", durationMs);
            cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
            return (long)cmd.ExecuteScalar()!;
        }
    }

    public IReadOnlyList<Transcription> History(int limit = 200) =>
        QueryTranscriptions("SELECT * FROM transcriptions ORDER BY created_at DESC LIMIT $limit", ("$limit", limit));

    /// <summary>Full-text search. Plain words are matched as prefixes ("рубл" finds "рублей").</summary>
    public IReadOnlyList<Transcription> Search(string query, int limit = 200)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => "\"" + t.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"*");
        var match = string.Join(' ', terms);
        if (match.Length == 0)
        {
            return History(limit);
        }

        return QueryTranscriptions("""
            SELECT t.* FROM transcriptions_fts fts JOIN transcriptions t ON t.id = fts.rowid
            WHERE transcriptions_fts MATCH $q ORDER BY t.created_at DESC LIMIT $limit
            """, ("$q", match), ("$limit", limit));
    }

    public void DeleteTranscription(long id) => Execute("DELETE FROM transcriptions WHERE id = $id", ("$id", id));

    public void ClearHistory() => Execute("DELETE FROM transcriptions");

    public void AddDictionaryEntry(string wrong, string correct, string lang, string source)
    {
        Execute("""
            INSERT INTO dictionary (wrong, correct, lang, source, created_at) VALUES ($w, $c, $l, $s, $t)
            ON CONFLICT(wrong, lang) DO UPDATE SET correct = excluded.correct, source = excluded.source
            """, ("$w", wrong), ("$c", correct), ("$l", lang), ("$s", source),
            ("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0));
    }

    public void DeleteDictionaryEntry(long id) => Execute("DELETE FROM dictionary WHERE id = $id", ("$id", id));

    public IReadOnlyList<DictionaryEntry> DictionaryEntries()
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, wrong, correct, lang, source FROM dictionary ORDER BY wrong";
            using var r = cmd.ExecuteReader();
            var list = new List<DictionaryEntry>();
            while (r.Read())
            {
                list.Add(new DictionaryEntry(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
            }

            return list;
        }
    }

    /// <summary>{wrong → correct} for <see cref="Text.UserCorrections.Apply"/>.</summary>
    public IReadOnlyDictionary<string, string> Corrections() =>
        DictionaryEntries().GroupBy(e => e.Wrong).ToDictionary(g => g.Key, g => g.First().Correct);

    public IReadOnlyDictionary<string, string> LoadCandidates()
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT wrong, correct FROM correction_candidates";
            using var r = cmd.ExecuteReader();
            var candidates = new Dictionary<string, string>();
            while (r.Read())
            {
                candidates[r.GetString(0)] = r.GetString(1);
            }

            return candidates;
        }
    }

    public void SaveCandidate(string wrong, string correct) => Execute("""
        INSERT INTO correction_candidates (wrong, correct, created_at) VALUES ($w, $c, $t)
        ON CONFLICT(wrong) DO UPDATE SET correct = excluded.correct, created_at = excluded.created_at
        """, ("$w", wrong), ("$c", correct), ("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0));

    public void RemoveCandidate(string wrong) => Execute("DELETE FROM correction_candidates WHERE wrong = $w", ("$w", wrong));

    public void ClearCandidates() => Execute("DELETE FROM correction_candidates");

    public void Dispose() => _connection.Dispose();

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.AddWithValue(name, value);
            }

            cmd.ExecuteNonQuery();
        }
    }

    private List<Transcription> QueryTranscriptions(string sql, params (string Name, object Value)[] parameters)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.AddWithValue(name, value);
            }

            using var r = cmd.ExecuteReader();
            var list = new List<Transcription>();
            while (r.Read())
            {
                list.Add(new Transcription(
                    r.GetInt64(r.GetOrdinal("id")),
                    r.GetString(r.GetOrdinal("raw_text")),
                    r.GetString(r.GetOrdinal("processed_text")),
                    r.GetString(r.GetOrdinal("model")),
                    r.GetString(r.GetOrdinal("lang")),
                    r.GetInt64(r.GetOrdinal("duration_ms")),
                    DateTimeOffset.FromUnixTimeMilliseconds((long)(r.GetDouble(r.GetOrdinal("created_at")) * 1000))));
            }

            return list;
        }
    }
}
