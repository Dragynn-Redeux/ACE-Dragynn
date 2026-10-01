# Instanced landblocks

An **instance** is a separate, private copy of some landblocks. Two players in two instances of the same landblock never see, hear, hit or collide with each other, and nothing in an instance is ever saved. Instance 0 is the persistent world, which is everything that existed before instances.

This is used for three things:

- **Dungeons for one group** (the capstone dungeons): one instance per fellowship, instead of the six hand-made copies of each dungeon.
- **Islands**: whole groups of landblocks, made as instances. An island can be *instance only*, so that it does not exist in the persistent world at all.
- **The training academies**: every new character starts in an instance of the academy of their starter town that is theirs alone.

## How it works

| Word | Meaning |
|---|---|
| **Template** (`InstanceTemplate`) | What an instance is made of: its landblocks (the **footprint**), where players arrive, and where they are sent when it ends. |
| **Footprint** | The landblocks of the instance. Nothing else is ever loaded for it, whatever is next to them in the world. |
| **Ring / boundary** | Landblocks of the footprint that are a margin around the place. They are loaded, but players are turned back when they get into one. |
| **Instance only** | The template's landblocks stop existing in the persistent world. |
| **Instance** (`WorldInstance`) | One live copy made from a template. It has an id (1, 2, 3, ...), never reused while the server runs. |

Everything that belongs to an instance carries its id: the landblocks (`Landblock.Instance`), the objects in them (`WorldObject.InstanceId`), and the physics cells and objects (`ObjCell.Instance`, `PhysicsObj.Instance`). Landblocks of different instances are never ticked in the same `LandblockGroup`, and lookups from physics into an instance never load anything.

### What is guaranteed

- **Nothing leaks between instances.** Objects, players, physics, visibility, monsters' targets and spawns are per instance. Anything that spawns something (a pet, a projectile, a portal, loot) puts it in the instance of whoever made it.
- **Fellowships and pets are per instance too.** A fellowship member in another instance is not "near", whatever their coordinates are: they get no shared kill XP and no kill credit (quest XP is not about distance, so it is still shared). A pet whose owner is in another instance is destroyed, as it is when the owner teleports away. Houses can't be bought or rented in an instance, because the house would be a copy that is deleted with it.
- **Nothing is saved.** An instance loads nothing from the shard and saves nothing to it. Objects in it get their ids from a reserved range (`0x7FF00000`-`0x7FFFFFFF`, given back when the instance ends), so two copies of one landblock don't have two objects with the same id.
- **Guids written in the world database still work.** A weenie can name a static object by the guid the world database gave it: its `ActivationTarget` (a button that opens one particular door). In an instance that door has a guid of its own, so `Landblock.GetObjectFromWorldGuid` looks such a guid up as the copy of the object that is in the instance (each instance keeps a map from the world database's guids to its own, made as its landblocks load). Anything else that finds an object from a guid that was written down before the server was running has to do the same.
- **Nothing outlives its instance.** When an instance is deleted everything in it is destroyed.
- **Nobody ends up in an instance by accident.** A teleport with no instance stays in the instance the player is in only if the destination is inside its landblocks: a portal inside a dungeon, or a recall to a lifestone that is part of the island. A destination outside them (a recall to a lifestone somewhere else, an admin `/tele` to another place) goes to the persistent world.
- **The edge is solid.** Anything the server moves (monsters, pets, projectiles) stops at the edge of what an instance is made of, like at a wall. A player's client walks by itself and knows the whole world, so when it reports a position past the edge the server refuses it and puts the client back where it was (the way it does when a jump height is not possible): to the player it is a wall, with a little rubber-banding when they push on. The same is done in the persistent world next to a landblock that only exists in instances. If the server let such a position through, the client would end up somewhere the server does not have, and would throw away everything it was shown.

### Rules for what happens in an instance

- **No corpse.** A player who dies in an instance leaves no corpse and loses nothing, since the corpse would only be deleted along with the instance. They respawn at their bound lifestone by the same rule as every other teleport: in the instance if that lifestone is inside its landblocks (the lifestone of an island, say), in the persistent world if it is not.
- **No dropping items.** Dropping and splitting a stack onto the ground are refused, because whatever was dropped would be deleted with the instance.
- **Logging out.** A player who logs out inside an instance is saved at the template's return position (or at their sanctuary if it has none), so they log back in to the persistent world. A player who is found saved inside an instance-only landblock (the server went down while they were in it) is moved out when they log in.

### Lifetime

An instance ends `instance_empty_timeout_minutes` (default **15**) after its last player has left it. An instance nobody ever entered ends 15 minutes after it was made. `/instance close <id>` sends everyone out and deletes it as soon as they are gone.

## Islands

An island is a template that is read from `instances.json`, next to the server. The build copies the one in `apps/server` there only when there is none yet, and never over one that is there, so the copy next to the server is the one to edit, and a rebuild does not undo it (delete it and build to get the default back). It is read once, when the server starts, before the world opens. The file that comes with the server lists three islands for testing: `aerlinthe` (13 x 12 landblocks, 210 with the ring), `test-holtburg` (one landblock, 9 with the ring) and `test-big` (3 x 3 landblocks, 25 with the ring). None is instance only, so nothing changes in the persistent world until somebody runs `/instance open <name>`. A copy next to the server that was made before they were added does not get them, because a build never overwrites that copy: delete the copy and build, or copy the islands in. The file allows comments and trailing commas.

```json
{
  "islands": [
    {
      "name": "hebian-island",

      // the landblocks of the island: the first four digits of a cell (0xE74E0019 is landblock E74E).
      // "landblocks" lists them one by one and "rectangles" gives every landblock from one corner to the other
      "landblocks": [ "E74E" ],
      "rectangles": [ { "from": "E750", "to": "E852" } ],

      // 0 to 4 landblocks that are loaded around the island as a margin. Default 1.
      "bufferRing": 1,

      // required: true means the persistent world no longer has these landblocks
      "instanceOnly": true,

      // where players arrive. Inside the island itself, not in its ring.
      "entry": { "cell": "0xE74E0019", "x": 84, "y": 7.1, "z": 94, "qw": 1 },

      // optional, where players go when it ends. Leave it out, or write it as { }, for their sanctuary. Not inside the island.
      "return": { "cell": "0xA9B40019", "x": 84, "y": 7.1, "z": 94, "qw": 1 }
    }
  ]
}
```

Positions are a cell, `x`, `y`, `z`, and a rotation as `qx`, `qy`, `qz`, `qw` (no rotation is `qw` 1 and the others 0, which is what you get by leaving them out). Take them from `/loc` in the game. **For an outdoor cell the server works out the cell from `x` and `y`**, so use a cell and coordinates that match.

A mistake in one island is logged (`[INSTANCE] instances.json: island 'x': ...`) and only leaves that island out. These are refused: no name, a name used twice, names that start with `capstone:`, `instanceOnly` missing (it has to be said on purpose, see below) or written in quotes, any other value of the wrong kind (text where a number or a list belongs), no landblocks or one that is not written like `E74E`, more than 400 landblocks with the ring, an entry that is not in the island's own landblocks, and a return position inside the island or in another island that is instance only. Text that is not JSON, or a file that is not an object with a list of `islands`, can't be read at all, and then no island loads.

### The ring

A footprint ends where nothing is loaded. A player can travel a long way between two position updates, so without a margin someone who is knocked back, or is fast, could end up in a place with no landblock. So an island is loaded with a ring of landblocks around it, and the moment a player gets into one the server turns them back (`InstanceManager.OnPlayerMoved`): they are teleported to the place they were last in the island itself (or to the entry position if they have not been anywhere yet) and told "You can go no further that way." (A client that reports a position outside the footprint altogether is put back before it gets there: see "The edge is solid".)

The ring only exists to keep players on ground that is loaded. Put nothing there: creatures that would spawn right at the outer edge of the footprint can't be placed, because the edge is solid.

### Instance only

`"instanceOnly": true` says these landblocks exist **only** as instances. Then:

- the persistent world refuses to load them (`LandblockManager.GetLandblock` returns null and logs `[INSTANCE] Something asked the persistent world for landblock ...` once for each landblock), they are not loaded as neighbours of the landblocks next to them, and nobody can walk into them from there;
- `Player.Teleport` to them without an instance is refused ("That place is no longer there.");
- `/tele` and the advocate map teleport say that the landblock only exists as an instance.

It is destructive, so it has to be written down as `true` or `false`: an island for a place that is in use in the persistent world stops existing there. **Don't make landblocks that have player housing instance only.** Nothing loads the houses of a landblock that is refused, and their owners would lose them.

A template has to be registered before the world opens for this to be complete. If an instance-only template is registered while its landblocks are already loaded in the persistent world, that is logged as a warning and they stay loaded.

## Getting players in

Nothing sends players into an island by itself. For testing there are admin commands, and for content there is the API.

### Commands (admin)

`/instance` (admin):

| Command | Does |
|---|---|
| `/instance` or `/instance list` | The templates that are set up and the instances that are open, and how long the empty ones have left. |
| `/instance info [player \| 0xguid]` | Which instance you are in, and where in it (inside, in the ring, outside), and the same for the object you have selected (the last one you appraised). With a player's name, or the guid of **any** object in the world, for that one instead. It says `MISMATCH` if an object, its landblock, its physics and its cell don't agree on the instance, which means something moved it without moving all of them. |
| `/instance open <template> [new]` | Goes into the instance of a template (the one shared instance, made if there is none). `new` makes another one. |
| `/instance here [radius]` | Makes a private copy of the landblock you are in, and the ones within `radius` (up to 3) around it, and takes you in. |
| `/instance enter <id> [player]` | Goes into an open instance, or sends the player. `0` is the persistent world. Players arrive where the template says. |
| `/instance leave [player]` | Goes back to where the instance sends players, or sends the player. |
| `/instance move <id> [0xguid]` | Moves the object you have selected, or the one with that guid, into instance `<id>` (0 is the persistent world), at the place where it is now. Refused if that place is not one of the instance's landblocks, or if the object is already in instance `<id>` (a move to where it is would leave it in the world but invisible). Only objects that were made while the server was running and are lying on the ground can be moved: not players, not the objects a landblock is made of (statics), not generators, and not what a generator made. |
| `/instance close <id>` | Shuts an instance down, sending everyone in it out. |

The guid works from anywhere, because an object in another instance can't be selected: you can't see it. You can get the guid from `/getinfo` while you are in the instance, or from the log line that `/ci` writes when it creates something.

Other commands know about instances too:

| Command | In an instance |
|---|---|
| `/myloc`, `/getinfo` | Show the instance of you, or of the selected object. |
| `/teleto <player>`, `/teletome <player>`, `/movetome` (on a player), `/teleallto` | Go to, or bring the player to, the instance the destination is in. |
| `/create`, `/ci`, `/createnamed`, `/createliveops`, `/moveto` | What you make appears in **your** instance. |
| `/tele`, `/teleloc`, `/telepoi`, `/telexyz`, and the other commands that go to coordinates | You stay in your instance if the place is inside its landblocks, and go to the persistent world if not. Nothing takes you into an instance by coordinates. |
| `@capstone` | Also lists the capstone dungeons that are open as instances. |
| `/createinst`, `/addenc`, `/removeenc` | Refused: they change the world database from where you stand, and a landblock in an instance has the coordinates of the real one, so the change would land on the real one. `/nudge`, `/rotate` and `/removeinst` work on the object you select, and refuse the statics of an instance, which are not rows in the world database. |

### From code

```csharp
var template = InstanceManager.GetTemplate("hebian-island");

// one instance for everybody. Give an owner (a fellowship, a player) for one each: the same owner always gets the same instance
var instance = InstanceManager.FindOrCreate(template, owner: null, out var created);

InstanceManager.Enter(player, instance);   // to the template's entry position
InstanceManager.Leave(player);             // to where the template sends players
```

`FindOrCreate` finds and makes in one step, so two players who arrive at the same moment get the same instance. `created` is true for the call that made it, which is when whatever the instance needs (the fellowship's modifiers, for a capstone) is set up. A template made in code can be given to `InstanceManager.RegisterTemplate` to get a name (and to make it instance only).

## Capstone dungeons

On for every capstone dungeon. The server property `capstone_instanced_dungeons` is `*`, or a comma separated list of dungeon names (as they are in the `AssignCapstoneDungeon` emote), and the dungeons it names open a private instance of the original landblock for each fellowship, instead of one of the numbered copies. `*` is the default, and it means all 18 now (the 15 capstone dungeons, the second parts of Lugian Mines (`Lugian Mines2`) and Mines of Despair (`Beyond the Mines`), which are instances of their own, and the Olthoi Queen's Lair raid) and any that is added later. To use the copies for some, list the ones that should be instances, or empty the property for all of them.

**A value that is saved in the shard database is used instead of the default.** A server that ran before the default was changed still has the old default (empty) saved, and nothing changes there until it is saved again: `/modifystring capstone_instanced_dungeons *` (no quotes needed, and short enough to type in the client), or the shard update `2026-09-27-00-Capstone-Instanced-Dungeons.sql`, which only runs where `AutoApplyDatabaseUpdates` is on. The log at startup says what is in force (`[INSTANCE] capstone_instanced_dungeons is *, so every capstone dungeon opens as an instance`), and `/fetchstring capstone_instanced_dungeons` shows the value first, in lines the client can show. A name has to match a whole dungeon name: `Glenden` is not `Glenden Wood Dungeon`.

The modifiers the leader chose are kept by the fellowship for as long as it has the dungeon, so an instance made again after the last one was deleted, or a copy loaded again after it was unloaded, gets the same ones. A capstone entrance portal (one whose Portal emote leads to `AssignCapstoneDungeon`, straight away or after a quest check like the Olthoi Passage Door's) sends the player straight to their instance or copy, without going to the portal's own destination first. A player it doesn't send on (no fellowship, a failed quest check) stays at the portal. That goes for a recall or a summoned portal of one too.

## Training academies

Every new character starts in the training academy of their starter town: landblock `20FC` for Shoushi, `20FD` for Yaraq and `20FE` for Holtburg. They are three copies of the same dungeon (a Life Stone, a chest for each class, sparring golems, and two exit portals to the town). Each is an instance template that is **personal** (`InstanceTemplate.Personal`), named `academy:shoushi`, `academy:yaraq` and `academy:holtburg`. They are made in code (`StarterAcademies`, which is also where new characters get their start position from) and registered at startup, after `instances.json`, so `/instance list` shows them.

A personal template gives every player who logs in inside its landblocks an instance of their own, so no two characters ever share an academy:

- As a player logs in, just before they enter the world (`InstanceManager.AssignPersonalInstance`, called from `WorldManager.DoPlayerEnterWorld`), a player who is standing in a personal template's landblock gets a new instance of it and enters the world in it. That is every new character (they are saved at the start of their academy) and everyone who logged out, or lost their connection, inside an academy.
- A player who logs out inside a personal instance is saved **where they are**, not at the template's return position, and gets a new instance there when they log in again, so they carry on from where they left off. Their quests, items and vitae belong to the character, so nothing about the academy starts again.
- Everything else works as in any instance. The exit portals lead to the persistent world (and still set the sanctuary to the outpost in town, as they did). A player who dies in an academy respawns at their bound lifestone in the same instance (the Life Stone of the academy, or the start position). The template's return position, which is where `/instance leave` and `/instance close` send players, is the town that the first exit portal leads to.
- The academies in the persistent world stay: they are not instance only, so a builder can still `/tele` there and the content commands work. Nobody arrives there by logging in. A player who is in an academy instance and needs the persistent one uses `/instance leave` and then `/tele`.
- If the instance can't be made, the error is logged (`[INSTANCE] Could not make an instance of ...`) and the player enters the persistent academy instead. Logging in never fails because of an instance.
- The instance of an academy is deleted `instance_empty_timeout_minutes` after its player has left it (15 minutes by default), although nobody can ever go into it again.
- Two new characters can't see each other, group up, trade or use local chat in the academy, because they are in different instances. They meet in the town.

`/instance open academy:holtburg` makes one shared instance for testing, like for any template. To turn the whole thing off, use `/modifybool starter_academy_instances false`: new logins enter the persistent academies again, and nobody who is in an instance is moved.

## Server properties

| Property | Default | |
|---|---|---|
| `instance_empty_timeout_minutes` | 15 | How long an instance stays open after the last player has left it. Change with `/modifylong`. |
| `capstone_instanced_dungeons` | `*` (every capstone dungeon, and the Olthoi Queen's Lair) | The capstone dungeons that are opened as instances: `*`, or a comma separated list of names. A value already saved in the database wins over the default. |
| `starter_academy_instances` | true | Whether a player who logs in inside a training academy (every new character) gets an instance of it of their own. Change with `/modifybool`. It only affects logins. |

## Performance

- An instance landblock costs what a persistent one does (all its cells, statics, monsters and spawns), and an island is that many of them: 3 x 3 landblocks with a ring of one is 25. They are all kept loaded for as long as the instance is open, whether or not anyone is in them. The ones that no player is near go dormant after a minute like any landblock (monster AI and physics stop, but generators keep running) and wake when a player comes near, so a big island with one player in it does not run every monster in it. Landblocks of an instance that lie together are one landblock group, so one thread ticks a contiguous island whole.
- Creating an instance loads all of its landblocks, on the thread that asks (their DAT data and their world database content), which is noticeable for a big island. Do it when it is quiet, or make the shared instance early.
- **Measured** (2026-09-20, an island of 210 landblocks with its ring, on a development server with one player): opening it paused the server for 3 to 4 seconds. Memory went from 1,206 MB to 1,270 MB (64 MB, about 0.3 MB for each landblock), and loaded landblocks from 10 to 220. There were about 1,700 more objects (statics and the like), one more landblock group, and no database rows. The first instance of an island also fills caches that stay afterwards (about 480 more files from `Cell.dat`, 240 from `Portal.dat`, 200 sets of landblock content from the world database), so the next one should be cheaper. It also used about 4,400 dynamic guids in those first seconds, for what the island holds (what monsters carry and what generators spawn). In a second run, watched for six minutes after that, the island used 20,800 more, mostly in the first five while it filled in (from about 2,000 objects to 3,000), and only about 2,700 of them were given back when it was deleted. A guid that is given back is held for six hours before it is used again, and why the others were not given back is not known yet (test plan, J9). The CPU, object and memory numbers of that run are in the test plan, section 7. With the dormancy fix, 201 of the island's 210 landblocks were dormant 98 seconds after it opened (the 9 next to the player, and the one persistent landblock that is kept loaded, stayed awake), the island stayed one landblock group, and it cost about 4 to 6 CPU seconds a minute more than the same server without it, which is roughly a tenth of one core or less. It held no monsters in that run, so what stopping monster AI saves was not measured (test plan, section 7).
- The persistent world pays almost nothing while there are no instances or instance-only landblocks: a read of one field per landblock lookup. With instance-only landblocks it is one set lookup.
- Static objects in instances take their ids from a pool of about a million, and get them back when the instance ends, after a six hour wait (so an id that a client may still remember is not given out again at once). If every id has been used and none has waited that long, the one that has been back the longest is used early and a warning is logged. If all of them are in use, that is logged as fatal and the object is not made.
- Instances are ticked in their own landblock groups, so they run in parallel like other landblocks when multi-threaded ticking is on.

## Known gaps

- Not run against a live server yet (there is no database in the environment it was written in): the capstone path, `/instance`, and the real player flows (teleport, login, logout, death). The pieces below them (physics, landblocks, ids, teardown) are tested, both with unit tests and against the real DATs.
- Portal storm zones (`ResonanceZoneService`) group players by landblock number and never look at the instance, so a storm in a landblock also reaches the players in instances of it. Decide whether that is wanted.
- Only `ActivationTarget` is translated. The world database was checked for weenies that name a static object by its guid (`SELECT object_Id, type, value FROM weenie_properties_i_i_d WHERE value BETWEEN 1879048192 AND 2147483647`): 38 weenies, all `ActivationTarget`, pointing at 35 statics in 18 landblocks (none of them in a capstone dungeon), and nothing else. A guid written down anywhere else (a new property, an emote) would not be translated. The links between statics (`landblock_instance_link`) are not affected, because they are made as references between the objects.
- Admin `Create*` commands and the old `Game.cs` chess pieces don't copy the instance to what they make. The `[INSTANCE]` warning in the log says when something is spawned without one.
- Spawns that would be placed right at the outer edge of a footprint fail, because the edge is solid (see the ring).
- Islands are made from a file, and there is nothing in the game yet that sends a player into one. The training academies are the only thing that puts players in instances by itself.

## Testing

- `apps/server-tests`: `InstanceManagerTests`, `InstanceTemplateTests` (templates, rings and `instances.json`), `InstancePhysicsTests`, `EphemeralStaticGuidTests`, `LandblockGroupTests`, `LandblockIdTests`, `StarterAcademyTests`. They need no DATs or database.
- Against the real DATs, with no database: a console project that references `ACE.Server.csproj`, calls `ConfigManager.Initialize(Config.js)`, `DatManager.Initialize(<dat folder>)`, `new PhysicsEngine(new ObjectMaint(), new SmartBox()) { Server = true }`, and then makes instances of real landblocks with `InstanceManager.Create()`. Objects are placed with `Landblock.AddWorldObject`, and movement is tried with `PhysicsObj.transition()`. (The tests that fail in a fresh checkout, `Sphere_*`, `CanParseStarterGearJson` and the `*_Initialize` ones, fail the same way without any of this: they need a database, DATs and config.)
