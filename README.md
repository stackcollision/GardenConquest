# GardenConquest
A new game mode for [Space Engineers](http://www.spaceengineersgame.com/) that
provides players with additional PVP opportunities and helps admins limit grid
count and complexity.

## Gameplay Concepts

### Ship Classification
Each grid must have a special Hull Classifier beacon on it.  This block
requires a new component named "Ship License" to build. The larger the class,
the greater the number of licenses required.

Each faction is also permitted a limited number of "Unlicensed" grids
(default 2). An Unlicensed Hull Classifier is required even on these grids.
These classifiers do not require Ship Licenses to build, but they have
relatively low block limits.

Classifiers and their License cost (subject to change):

* Unlicensed - 0
* Fighter - 5
* Corvette - 10
* Utility - 15
* Frigate - 20
* Destroyer - 40
* Cruiser - 80
* Carrier - 100
* Battleship - 150

Until a Hull Classifier is built on a grid, or after a classifier is destroyed,
the grid becomes "Unclassified". This starts a timer (default 2 hours) until the
grid becomes a "Derelict." A Derelict is immediately disabled and eventually
destroyed. Players should classify their grids within the time limit to ensure
they are not damaged/removed.

#### Class-based block limits
Each class imposes limits on the number of various blocks on the grid,
i.e. turrets, as well as the total block count. Players are unable to add blocks
over the established limits for the grid's class. If a player changes a grid's
class and it is breaking the limits for the new class, no new blocks can be
added until the offending blocks are removed. Server owners can set specific
limits per class for certain grids. The defaults are (currently in flux):

* Fighter - 0 turrets,
* Corvette - 2 turrets,

...etc, will be filled out when these are more certain...

### Control Points
In order to acquire Ship Licenses, a faction must hold a Control Points ("CPs").
Every X minutes (default 15), a CP "round" will end and the CP's reward
of Y Ship Licenses (default 5) will be given to the faction with the most valid grids
within the CP's sphere of influence.

A grid is counted towards a faction's total if:
* It has a powered, broadcasting Hull Classifier beacon
* The broadcast range on the classifier is at least as large as the radius of
the CP (default 15km). This is to prevent players from hiding while capturing a
CP.

If there is a tie in grid counts between factions, no one gets the reward.

### Rule Enforcement

Class-based rule enforcement is applied in two ways:

* Immediately when you try to place a block. If it would bring you over the
  limit, it's removed.
* Over time if your Ship/Station goes over the limit itself, i.e.
	* its classifier is removed/destroyed
	* you replace its classifier with one that has lower limits
	* it merges with another grid
	* it was created before the mod was applied

### Example Gameplay Walkthrough

When you first place a new grid, it will be **Unclassified**:
* you can place up to 25 of any block
* you're not allowed any of these per player/faction, so it will eventually be removed

When a grid loads into a server with this mod enabled, it will be
**Unclassified**:
* you can't place any new blocks until you have less than 25
* any existing blocks over 25 will eventually be removed
* you're not allowed any per player/faction, so it will eventually be removed.

To get around these issues, use/adapt the provided spawn ship or gather enough
materials to build your own **Unlicensed** Hull Classifier:
* you can place up to 100 blocks, with certain limits.
* anything over 100 will eventually be removed
* you're allowed 2 per player/faction.
* These can have some weaponry on them.

Use these (plus any existing ships that haven't yet been removed) to capture
 CPs and get **License Components**.

You can use those to build bigger and better Hull Classifiers. These will
allow you to place and keep more blocks

## Deployment

Use [this builder](https://github.com/zrisher/se_mod_builder).

## Contributing

See the [Contributing section of our wiki]
(https://github.com/stackcollision/GardenConquest/wiki/Contributing) for
instructions on getting up and running in development, as well as our policies
on PRs, Testing, and Code Quality.
