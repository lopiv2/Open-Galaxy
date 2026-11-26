# Naves con `allegiance` sin definir en `Allegiances.json`

Fecha: 2026-11-26. Fuente: `Assets/Resources/files/ShipTypes.json` vs claves de
`Assets/Resources/files/Allegiances.json`
(`rebel`, `newrepublic`, `imperial`, `remnant`, `vong`, `republic`, `sith`,
`krayt`, `galacticalliance`, `corporatesector`, `smuggler`, `pirate`, `hutt`,
`independent`, `civilian`, `hostile`).

## Por qué existe este archivo

Cuando una nave IA tiene un `allegiance` sin entrada en `Allegiances.json`,
`EnergyWeaponFunctions.SetEnergyWeaponCollisionLayers()` no encuentra
coincidencia, deja `allegiance` en `null` y lanza `NullReferenceException`
cada frame al disparar ( congela el juego si Error Pause está activo ).
Lo mismo afecta al lookup de capa de equipo en
`SceneFunctions.LoadSingleShip()`. Ya resuelto para `empire` (180 naves
migradas a `imperial` en este mismo cambio). Quedan **158 naves en
10 allegiances** pendientes de mapeo de diseño (crear la entrada en
`Allegiances.json` o reasignarlas a una facción existente).

Formato: `type` | displayName | prefab.

## blacksun (21)

- aeg77_vigo_transport | Aeg77 Vigo Transport | bsun_aeg77_vigo_transport
- ixiyen_mkii_01 | Ixiyen MkII 01 | bsun_Ixiyen_mkII_01
- ixiyen_mkii_02 | Ixiyen MkII 02 | bsun_ixiyen_mkII_02
- ixiyen_mki_01 | Ixiyen MkI 01 | bsun_ixiyen_mkI_01
- ixiyen_mki_02 | Ixiyen MkI 02 | bsun_ixiyen_mkI_02
- kasmir_ship | Kasmir Ship | bsun_kasmir_ship
- kihraxz_mkii_01 | Kihraxz MkII 01 | bsun_kihraxz_mkII_01
- kihraxz_mkii_02 | Kihraxz MkII 02 | bsun_kihraxz_mkII_02
- kihraxz_mkii_03 | Kihraxz MkII 03 | bsun_kihraxz_mkII_03
- kihraxz_mkii_04 | Kihraxz MkII 04 | bsun_kihraxz_mkII_04
- kihraxz_mkii_05 | Kihraxz MkII 05 | bsun_kihraxz_mkII_05
- kihraxz_mkii_06 | Kihraxz MkII 06 | bsun_kihraxz_mkII_06
- kihraxz_mki_01 | Kihraxz MkI 01 | bsun_kihraxz_mkI_01
- kihraxz_mki_02 | Kihraxz MkI 02 | bsun_kihraxz_mkI_02
- kihraxz_mki_03 | Kihraxz MkI 03 | bsun_kihraxz_mkI_03
- kihraxz_mki_04 | Kihraxz MkI 04 | bsun_kihraxz_mkI_04
- kihraxz_mki_05 | Kihraxz MkI 05 | bsun_kihraxz_mkI_05
- kihraxz_mki_06 | Kihraxz MkI 06 | bsun_kihraxz_mkI_06
- skyhook | Skyhook | bsun_skyhook
- surronian_assault_ship | Surronian Assault Ship | bsun_surronian_assault_ship
- xizor_transport_gozanti | Xizor Transport Gozanti | bsun_xizor_transport_gozanti

## cis (24)

- cis_frigate | Cis Frigate | cis_frigate
- cutlass9_patrol_fighter | Cutlass9 Patrol Fighter | cis_cutlass9_patrol_fighter
- doomtreader | Doomtreader | cis_doomtreader
- hyena | Hyena | cis_hyena
- lucrehulk | Lucrehulk | cis_lucrehulk
- missile_frigate | Missile Frigate | cis_missile_frigate
- munificent | Munificent | cis_munificent
- nantex | Nantex | cis_nantex
- procurator | Procurator | cis_procurator
- providence | Providence | cis_providence
- punworcca | Punworcca | cis_punworcca
- recusant | Recusant | cis_recusant
- sheathipede | Sheathipede | cis_sheathipede
- sheathipede2 | Sheathipede2 | cis_sheathipede2
- shuttle | Shuttle | cis_shuttle
- technounion | Technounion | cis_technounion
- technounion_destroyer | Technounion Destroyer | cis_technounion_destroyer
- trident | Trident | cis_trident
- tridroid | Tridroid | cis_tridroid
- valahari_mk1 | Valahari Mk1 | cis_valahari_mk1
- valahari_mk2 | Valahari Mk2 | cis_valahari_mk2
- vulture | Vulture | cis_vulture
- wavecrest | Wavecrest | cis_wavecrest
- zenuas_33_umbaran_starfighter | Zenuas 33 Umbaran Starfighter | cis_zenuas_33_umbaran_starfighter

## felempire (10)

- acclamator_mk3 | Acclamator Mk3 | fel_acclamator_mk3
- ardent_frigate | Ardent Frigate | fel_ardent_frigate
- arrow_corvette | Arrow Corvette | fel_arrow_corvette
- galaxy_destroyer | Galaxy Destroyer | fel_galaxy_destroyer
- nune_shuttle | Nune Shuttle | fel_nune_shuttle
- pelleaon | Pelleaon | fel_pelleaon
- phoebos_class_fighter | Phoebos Class Fighter | fel_phoebos_class_fighter
- sigma_shuttle | Sigma Shuttle | fel_sigma_shuttle
- tie_neutralizer | TIE Neutralizer | fel_tie_neutralizer
- tie_predator | TIE Predator | fel_tie_predator

## firstorder (7)

- finalizer | Finalizer | fo_finalizer
- fo_tie_bomber | FO TIE Bomber | fo_tie_bomber
- fo_tie_fighter | FO TIE Fighter | fo_tie_fighter
- fo_tie_fighter_elite | FO TIE Fighter Elite | fo_tie_fighter_elite
- fo_tie_interceptor | FO TIE Interceptor | fo_tie_interceptor
- supremacy | Supremacy | fo_supremacy
- upsilon_shuttle | Upsilon Shuttle | fo_upsilon_shuttle

## kraytempire (5)

- anihilator_fighter | Anihilator Fighter | krayt_anihilator_fighter
- assassin_corvette | Assassin Corvette | krayt_assassin_corvette
- dragon | Dragon | krayt_dragon
- dragon_advanced | Dragon Advanced | krayt_dragon_advanced
- gladius_class_light_freighter | Gladius Class Light Freighter | krayt_gladius_class_light_freighter

## mandalorian (10)

- basilisk_wardroid | Basilisk Wardroid | man_basilisk_wardroid
- davaab | Davaab | man_davaab
- gauntlet_fighter | Gauntlet Fighter | man_gauntlet_fighter
- hondo_karr_ship | Hondo Karr Ship | man_hondo_karr_ship
- jehavey | Jehavey | man_jehavey
- kandosii | Kandosii | man_kandosii
- kyramud | Kyramud | man_kyramud
- mandalorian_cruiser_marki | Mandalorian Cruiser Marki | man_mandalorian_cruiser_marki
- mandalorian_cruiser_markii | Mandalorian Cruiser Markii | man_mandalorian_cruiser_markii
- teroch | Teroch | man_teroch

## misc (1)

- escapepod | Escapepod | misc_escapepod

## none (17)

- archammer | Archammer | imp_archammer
- cd273 | Cd273 | reb_cd273
- debris01_01 | Debris01 01 | debris01_01
- debris01_02 | Debris01 02 | debris01_02
- debris01_03 | Debris01 03 | debris01_03
- debris02_01 | Debris02 01 | debris02_01
- debris02_02 | Debris02 02 | debris02_02
- debris02_03 | Debris02 03 | debris02_03
- debris02_04 | Debris02 04 | debris02_04
- debris02_05 | Debris02 05 | debris02_05
- debris02_06 | Debris02 06 | debris02_06
- debris02_07 | Debris02 07 | debris02_07
- debris02_08 | Debris02 08 | debris02_08
- debris02_09 | Debris02 09 | debris02_09
- debris02_10 | Debris02 10 | debris02_10
- mc50 | Mc50 | reb_mc50
- procurator | Procurator | imp_procurator

## oldrepublic (36)

- aurek_fighter | Aurek Fighter | orep_aurek_fighter
- brute_patrolship | Brute Patrolship | orep_brute_patrolship
- bt7_thunderclap | Bt7 Thunderclap | orep_bt7_thunderclap
- chela | Chela | orep_chela
- ebon_hawk | Ebon Hawk | orep_ebon_hawk
- foray_corvette | Foray Corvette | orep_foray_corvette
- gunship | Gunship | orep_gunship
- hammerhead | Hammerhead | orep_hammerhead
- il_5_ocula_gunship | Il 5 Ocula Gunship | orep_il_5_ocula_gunship
- inexpugnable | Inexpugnable | orep_Inexpugnable
- ministryclassshuttle | Ministryclassshuttle | orep_ministryclassshuttle
- minstrel_class_space_yacht | Minstrel Class Space Yacht | hutt_minstrel_class_space_yacht
- nr2_light_transport | Nr2 Light Transport | orep_nr2_light_transport
- orbital_security_station | Orbital Security Station | orep_orbital_security_station
- paladin_class_destroyer | Paladin Class Destroyer | orep_paladin_class_destroyer
- praetorian_class_frigate | Praetorian Class Frigate | orep_praetorian_class_frigate
- quarrel_gunship_mk1 | Quarrel Gunship Mk1 | orep_quarrel_gunship_mk1
- quarrel_gunship_mk2 | Quarrel Gunship Mk2 | orep_quarrel_gunship_mk2
- quartermaster | Quartermaster | orep_quartermaster
- rampart_heavybomber_mark1 | Rampart Heavybomber Mark1 | orep_rampart_heavybomber_mark1
- rampart_heavybomber_mark4 | Rampart Heavybomber Mark4 | orep_rampart_heavybomber_mark4
- raven_bomber_mk1 | Raven Bomber Mk1 | orep_raven_bomber_mk1
- raven_bomber_mk2 | Raven Bomber Mk2 | orep_raven_bomber_mk2
- rep_dreadnaught | Rep Dreadnaught | orep_dreadnaught
- support_ship01 | Support Ship01 | orep_support_ship01
- support_ship02 | Support Ship02 | orep_support_ship02
- support_ship03 | Support Ship03 | orep_support_ship03
- talon_fighter | Talon Fighter | orep_talon_fighter
- talon_scout | Talon Scout | orep_talon_scout
- transport_shuttle | Transport Shuttle | orep_transport_shuttle
- transport_shuttle 1 | Transport Shuttle 1 | orep_transport_shuttle 1
- trireme | Trireme | orep_trireme
- troop_transport | Troop Transport | orep_troop_transport
- valor_class_cruiser | Valor Class Cruiser | orep_valor_class_cruiser
- xs_freighter | Xs Freighter | orep_xs_freighter
- zenith_cruiser | Zenith Cruiser | orep_zenith_cruiser

## sithempire (27)

- centurion | Centurion | orep_centurion
- derriphan | Derriphan | sith_derriphan
- interdictor_class_cruiser | Interdictor Class Cruiser | sith_interdictor_class_cruiser
- sith_battlecruiser_mark1 | Sith Battlecruiser Mark1 | sith_battlecruiser_mark1
- sith_battlecruiser_mark2 | Sith Battlecruiser Mark2 | sith_battlecruiser_mark2
- sith_battlecruiser_mark3 | Sith Battlecruiser Mark3 | sith_battlecruiser_mark3
- sith_bomber | Sith Bomber | sith_bomber
- sith_destroyer | Sith Destroyer | sith_destroyer
- sith_fighter | Sith Fighter | sith_fighter
- sith_frigate_mark1 | Sith Frigate Mark1 | sith_frigate_mark1
- sith_frigate_mark2 | Sith Frigate Mark2 | sith_frigate_mark2
- sith_frigate_mark3 | Sith Frigate Mark3 | sith_frigate_mark3
- sith_fury | Sith Fury | sith_fury
- sith_gunship | Sith Gunship | sith_gunship
- sith_interceptor | Sith Interceptor | sith_interceptor
- sith_light_interceptor | Sith Light Interceptor | sith_light_interceptor
- sith_scout | Sith Scout | sith_scout
- sith_shuttle | Sith Shuttle | sith_shuttle
- sith_striker_a | Sith Striker A | sith_striker_a
- sith_striker_b | Sith Striker B | sith_striker_b
- sith_striker_c | Sith Striker C | sith_striker_c
- sith_transport | Sith Transport | sith_transport
- sith_transport_shuttle | Sith Transport Shuttle | sith_transport_shuttle
- sith_troop_transport | Sith Troop Transport | sith_troop_transport
- starforge | Starforge | sith_starforge
- strongarm_gunship | Strongarm Gunship | sith_strongarm_gunship
- x70b_phantom | X70b Phantom | sith_x70b_phantom

## Notas de mapeo (pendiente de decisión de diseño)

- `sithempire` → candidato obvio `sith` (26 de 27 prefabs ya son `sith_*`).
- `kraytempire` → candidato `krayt` (prefabs `krayt_*`, ya existe la facción).
- `felempire` → probablemente nueva entrada Fel Empire o reutilizar `remnant`.
- `oldrepublic` → probablemente nueva entrada o `republic`.
- `cis`, `blacksun`, `mandalorian`, `firstorder` → probablemente nuevas entradas.
- `misc` (escapepod) y `none` (debris sobre todo) → quizá `civilian` o mantener `none`
  con fallback en código.
- El campo `era` no lo lee ningún script: no requiere cambios.
