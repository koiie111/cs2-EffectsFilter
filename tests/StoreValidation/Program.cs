using FourIll.EffectsFilter;
using MySqlConnector;

// The caller provides an isolated disposable Docker database, never vip_global.
var connection = Environment.GetEnvironmentVariable("FX_TEST_CONNECTION") ?? throw new Exception("FX_TEST_CONNECTION required");
var builder = new MySqlConnectionStringBuilder(connection);
if (!builder.Database.StartsWith("fx_validation_") || builder.Server != "127.0.0.1") throw new Exception("Only isolated local test databases allowed");
var store = new FxStore(connection, 35);
var otherServer = new FxStore(connection, 1);
await using var db = new MySqlConnection(connection);
await db.OpenAsync();
async Task Exec(string sql) { await using var cmd = new MySqlCommand(sql, db); await cmd.ExecuteNonQueryAsync(); }
async Task<long> Scalar(string sql) { await using var cmd = new MySqlCommand(sql, db); return Convert.ToInt64(await cmd.ExecuteScalarAsync()); }
void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
Check(!(await store.Load(76561198000000001, false)).Any(x => x), "default: all visible");
await Exec("""
INSERT INTO vip_test_known_players (steam_id64,first_seen_at,source) VALUES
(76561198000000002,UTC_TIMESTAMP()-INTERVAL 2 DAY,'seed'),
(76561198000000003,UTC_TIMESTAMP()-INTERVAL 8 DAY,'seed'),
(76561198000000004,UTC_TIMESTAMP()-INTERVAL 2 DAY,'seed'),
(76561198000000005,UTC_TIMESTAMP()-INTERVAL 7 DAY,'seed');
""");
Check((await store.Load(76561198000000002, true)).All(x => x), "opt-in new-player default");
Check(!(await store.Load(76561198000000003, true)).Any(x => x), "old player stays visible");
Check(!(await store.Load(76561198000000004, false)).Any(x => x), "default disabled for newcomers");
Check(!(await store.Load(76561198000000005, true)).Any(x => x), "exact seven-day cutoff excluded");
Check((await store.Load(76561198000000006, true)).All(x => x), "player missing from known_players is new");
Check(!(await otherServer.Load(76561198000000004, true)).Any(x => x), "existing preference wins over new default");
var hints = await Task.WhenAll(store.ClaimHint(76561198000000001), otherServer.ClaimHint(76561198000000001));
Check(hints.Count(x => x) == 1, "concurrent servers claim hint once");
Check(await Scalar("SELECT COUNT(*) FROM player_fx_events WHERE event='first_hint'") == 1, "one first_hint metric");
await store.Save(76561198000000001, [true,false,false,false], "toggle", "trails", true);
Check((await otherServer.Load(76561198000000001, false)).SequenceEqual(new[]{true,false,false,false}), "reconnect on another server preserves categories");
await store.Record(76561198000000001, "menu_open");
await store.Save(76561198000000001, [true,true,true,true], "hide_all", "all", true);
Check((await otherServer.Load(76561198000000001, false)).All(x => x), "hide_all persisted");
await otherServer.Save(76561198000000001, [false,false,false,false], "show_all", "all", false);
Check(!(await store.Load(76561198000000001, true)).Any(x => x), "show_all persisted");
Check(await Scalar("SELECT COUNT(*) FROM player_fx_events WHERE event='toggle' AND category='trails' AND hidden=1 AND server_id=35") == 1, "toggle metric contains server/category/state");
Check(await Scalar("SELECT COUNT(DISTINCT event) FROM player_fx_events") == 5, "all five metrics recorded");
// Force a real insert error even on MySQL installations with non-strict ENUM handling.
await Exec("CREATE TRIGGER fx_validation_failure BEFORE INSERT ON player_fx_events FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='injected test failure'");
try { await store.Save(76561198000000001, [true,true,true,true], "hide_all", "all", true); throw new Exception("injected failure ignored"); }
catch (MySqlException) { }
finally { await Exec("DROP TRIGGER fx_validation_failure"); }
Check(!(await store.Load(76561198000000001, false)).Any(x => x), "transaction rolls back on metric failure");
Console.WriteLine("Storage validation complete");
