# Main scene runtime gait regression

Normal spawn, goals, navigation and peer avoidance remain enabled.

|FPS|Animal|Travel m|Max planted error m|
|---:|---|---:|---:|
|30|Tyrannosaurus Rex|47.159|0.00000|
|30|Pachycephalosaurus|5.930|0.00000|
|30|Velociraptor|42.200|0.00000|
|30|Sauropelta|5.927|0.00000|
|30|Baurusuchus|14.622|0.00029|
|60|Tyrannosaurus Rex|101.457|0.00000|
|60|Pachycephalosaurus|12.498|0.00001|
|60|Velociraptor|104.692|0.00000|
|60|Sauropelta|14.423|0.00852|
|60|Baurusuchus|34.553|0.00034|

FAILED System.InvalidOperationException: Main play stance error: Sauropelta 0.0200 state=RandomWalk traffic=Inactive
  at JurassicWorld.Editor.DinosaurGaitAcceptance.TickMain () [0x000ba] in E:\Unity\JurassicWorld\Assets\Editor\DinosaurGaitAcceptance.Main.cs:84 