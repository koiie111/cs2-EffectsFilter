using MySqlConnector;

namespace FourIll.EffectsFilter;

// No game API calls: safe to use exclusively on the database worker.
internal sealed class FxStore(string connection, int serverId)
{
    private async Task<MySqlConnection> Connect()
    {
        var db = new MySqlConnection(connection);
        try { await db.OpenAsync(); return db; }
        catch { await db.DisposeAsync(); throw; }
    }
    public async Task<bool[]> Load(ulong steam, bool newHidden)
    {
        await using var db = await Connect();
        // Existing rows always win; the default only applies on the first observation.
        // No known_players row means a brand-new player (plan, section 7): VIP_Test may insert it after us.
        await using var insert = new MySqlCommand("""
            INSERT IGNORE INTO player_fx_prefs (steam_id64, hide_trails, hide_auras, hide_c4, hide_kill)
            SELECT @steam, n.hidden, n.hidden, n.hidden, n.hidden FROM
            (SELECT IF(@enabled AND NOT EXISTS(SELECT 1 FROM vip_test_known_players
            WHERE steam_id64=@steam AND first_seen_at <= UTC_TIMESTAMP() - INTERVAL 7 DAY),1,0) hidden) n
            """, db);
        insert.Parameters.AddWithValue("@steam", steam);
        insert.Parameters.AddWithValue("@enabled", newHidden);
        await insert.ExecuteNonQueryAsync();
        await using var read = new MySqlCommand("SELECT hide_trails,hide_auras,hide_c4,hide_kill FROM player_fx_prefs WHERE steam_id64=@steam", db);
        read.Parameters.AddWithValue("@steam", steam);
        await using var reader = await read.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Missing FX preferences");
        return Enumerable.Range(0, 4).Select(reader.GetBoolean).ToArray();
    }
    public async Task<bool> ClaimHint(ulong steam)
    {
        await using var db = await Connect();
        await using var tx = await db.BeginTransactionAsync();
        await using var claim = new MySqlCommand("UPDATE player_fx_prefs SET hint_shown_at=UTC_TIMESTAMP() WHERE steam_id64=@steam AND hint_shown_at IS NULL", db, tx);
        claim.Parameters.AddWithValue("@steam", steam);
        var claimed = await claim.ExecuteNonQueryAsync() == 1;
        if (claimed) await Event(db, tx, steam, "first_hint");
        await tx.CommitAsync();
        return claimed;
    }
    public async Task Save(ulong steam, bool[] snapshot, string name, string category, bool hidden)
    {
        await using var db = await Connect();
        await using var tx = await db.BeginTransactionAsync();
        await using var cmd = new MySqlCommand("UPDATE player_fx_prefs SET hide_trails=@a,hide_auras=@b,hide_c4=@c,hide_kill=@d WHERE steam_id64=@steam", db, tx);
        cmd.Parameters.AddWithValue("@steam", steam);
        for (var i = 0; i < 4; i++) cmd.Parameters.AddWithValue("@" + (char)('a' + i), snapshot[i]);
        await cmd.ExecuteNonQueryAsync();
        await Event(db, tx, steam, name, category, hidden);
        await tx.CommitAsync();
    }
    public async Task Record(ulong steam, string name)
    {
        await using var db = await Connect();
        await Event(db, null, steam, name);
    }
    private async Task Event(MySqlConnection db, MySqlTransaction? tx, ulong steam, string name, string? category = null, bool? hidden = null)
    {
        await using var cmd = new MySqlCommand("INSERT INTO player_fx_events (steam_id64,server_id,event,category,hidden) VALUES (@steam,@server,@event,@category,@hidden)", db, tx);
        cmd.Parameters.AddWithValue("@steam", steam); cmd.Parameters.AddWithValue("@server", serverId);
        cmd.Parameters.AddWithValue("@event", name); cmd.Parameters.AddWithValue("@category", (object?)category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hidden", (object?)hidden ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
