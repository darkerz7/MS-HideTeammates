# Hide Teammates for ModSharp
Hides Teammates on the entire map or distance

## Required packages:
1. [ModSharp](https://github.com/Kxnrl/modsharp-public)
2. [ClientPreferences](https://github.com/Kxnrl/modsharp-public/tree/master/Sharp.Modules/ClientPreferences)
3. [LocalizerManager](https://github.com/Kxnrl/modsharp-public/tree/master/Sharp.Modules/LocalizerManager)

## Installation:
1. Install `ClientPreferences` and `LocalizerManager`
2. Compile or copy MS-HideTeammates to `sharp/modules/MS-HideTeammates` folger
3. Copy HideTeammates.json to `sharp/locales` folger

## CVARs:
Cvar | Parameter | Description
--- | --- | ---
`ms_ht_enabled` | <0/1> | Enable/Disable plugin
`ms_ht_maximum` | <1000-8000> | The maximum distance a player can choose
`ms_ht_hideia` | <0/1> | Enable/Disable ignoring player attachments (ex. prop leader glow)

## Commands:
Client Command | Description
--- | ---
`ms_ht/ms_hide [<-1-CVAR_MAX_Distance>]` | (-1 - Disable, 0 - Enable on the entire map, 1-CVAR_MAX_Distance - Enable ont the Distance)
`ms_ht/ms_hide` | Toggle hide teammates on the entire map. Maybe replaced by menu later
`ms_htall/ms_hideall` | Toggle hide teammates on the entire map
