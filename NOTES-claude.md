# Состояние работы (C8, раздел 8 плана) — черновик для продолжения

Сделано: контракт `FxFilterApi` (IFxFilter, FxCategory). Сборка: `dotnet build FxFilterApi -c Release`.

Осталось:
1. Плагин `4ill.EffectsFilter`: capability `4ill:fx`; `!fx`/`!effects`/`!hidefx`; меню из 5 пунктов;
   prefs в `vip_global.player_fx_prefs` (асинхронно при входе, кеш на слот); события в `player_fx_events`;
   FakeConVar `fx_disable_trails|auras|c4|kill|rainbow`, `fx_new_player_hidden` (cfg/4ill_fx.cfg);
   подсказка один раз (`hint_shown_at`) без префикса [4ILL]; тексты — точно из раздела 8.
   При Unload выставлять флаг, чтобы IsHiddenFor/IsDisabledOnServer возвращали false (CSS не снимает capability).
2. Модули cs2-VIPCore (ветка claude/fx-filter): PlayerTrail(Trails), PlayerParticle/Pet/Outfit(Auras),
   C4Effect(C4), KillIcon(Kill), RainbowModel (cvar fx_disable_rainbow).
   - Доступ к IFxFilter — только через отдельный класс-мост с try/catch (FileNotFoundException/TypeLoadException),
     чтобы модули работали без FxFilterApi.dll (fail-open). Capability перезапрашивать раз в ~1 с.
   - FxFilterApi.dll положить в VIPCore/ рядом с VipCoreApi.dll, HintPath, Private=False.
   - Своё (owner == viewer) не трогать; скрытие объединять по ИЛИ с существующим «чужие».
   - KillIcon: если есть зрители с hide_kill — DontBroadcast + FireEventToClient (оригинальное оружие
     скрывающим); проверить на тестовом сервере, при рассинхроне не включать.
   - VIP_KillScreen — эффект только на экране убийцы, скрывать нечего.
3. dotnet build всех проектов, коммиты, `gh repo create koiie111/cs2-EffectsFilter`, push обеих веток. Не деплоить.
