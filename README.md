# Operation Black Market

Operation Black Market brings Rainbow Six Siege's maps, weapons and operators into SPT. Familiar buildings become places to raid, operator equipment turns up in the black market, and Rainbow's arsenal finds its way into Tarkov through Kali.

The setting is **The Compound**, an abandoned Rainbow training facility near Norvinsk. The operators left when the region fell apart. Their training grounds and equipment stayed behind.

## The Compound

House and Hereford share one raid map, with more Siege locations planned as the facility grows. You can move between training buildings, search for supplies and fight through spaces originally built for counter-terrorism exercises.

The map has its own loot containers, extraction points, bot spawns and locked rooms. The client plugin handles map lighting, sound, destruction and bot respawns, while the server connects the exported map data to SPT.

## The arsenal

The Items mod adds Rainbow weapons such as the R4-C, 416-C Carbine, F2 and Scorpion EVO, along with their skins, magazines, attachments and charms. They use Tarkov's weapon systems, with Siege firing and reload audio.

Kali runs the trader. Ash's outfit and voice are also included in the local mod package, alongside custom keys for the Compound's locked rooms.

## Development

This is a work in progress. The longer-term plan is a quest line connecting Prapor, Kali and the abandoned facility, with Rainbow equipment and weapons unlocked through progression. That quest line is still being developed.

This repository contains the C# sources for two mods:

| Project | Purpose |
| --- | --- |
| `OBMTheCompound` | The map's server integration and BepInEx client plugin |
| `OperationBlackMarketItems` | Weapons, Kali, Ash and item support |

Both target **SPT 4.0.13**, with .NET 9 server projects and netstandard2.1 client plugins. See [DEPENDENCIES.txt](DEPENDENCIES.txt) for the required and optional mods.

## Building from source

You need a local SPT installation and the dependencies listed above. Set its path when building:

```powershell
dotnet build OBMTheCompound/Server/OBMTheCompound.csproj -c Release -p:SptRoot="C:\SPT\SPT"
dotnet build OBMTheCompound/Client/OBMTheCompound.Client.csproj -c Release -p:GameRoot="C:\SPT"
dotnet build OperationBlackMarketItems/Server/OperationBlackMarketItems.csproj -c Release -p:SptRoot="C:\SPT\SPT"
dotnet build OperationBlackMarketItems/Client/R6PortClient.csproj -c Release -p:EftRoot="C:\SPT"
```

The build produces the server and client DLLs. Running them also requires the matching local mod data:

- **The Compound:** exported `data/location.json`, map bundles, configuration and `MapInfo` files.
- **Items:** item, clothing and voice definitions, Kali's trader data, bundle manifest and asset bundles.

The Unity projects, game assemblies, extracted models, textures, audio and compiled asset bundles are not stored in this repository. The data-generation tools are also outside this source snapshot, so these projects alone do not produce a complete installable package. Compatibility with SPT 4.1 or later has not been established.

## Credits

Created by **GodlyBDS**. Built on SPT, BepInEx, Harmony and WTT CommonLib, with the map integrations listed in the dependencies file. Development assistance from Claude and OpenAI Codex.

Rainbow Six Siege and Escape from Tarkov belong to their respective owners. This is an unofficial fan project.

## License

Source code is available under the [MIT License](LICENSE). Third-party libraries and game assets retain their own licenses.
