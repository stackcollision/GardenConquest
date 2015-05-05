# GardenConquest
A new conquest game mode for Space Engineers

## Gameplay Flow

### Ship Classification
Each grid must have a special classification beacon on it.  This beacon
requires a special resource "Ship License" to build. The larger the class, the
great the number of licenses required.  Each class imposes limitations on the
grid in terms of block and weapon counts.  Once the block count is reached no
new blocks can be added.

If a grid's classification beacon is destroyed, the grid becomes
"Unclassified".  This starts a timer (several hours) until the grid becomes a
derelict.  Once a grid becomes a derelict its functional blocks will be
destroyed.  If the grid is classified within the time limit it will not be
destroyed.

### Control Points
In order to acquire ship licenses a faction must hold Control Points (CPs) at
the time a round ends.  A faction is required to hold control points, even if
there is only one person in it.  At the time the round ends the CP's configured
reward will be given to the faction with the most valid grids in the CP's
sphere of influence.  If there is a tie in grid counts, no one gets anything.

A grid is counted towards capturing under the following conditions:
* It has power
* It has a classifier beacon
* The broadcast range on the classifier is large enough to encompass the CP's
center.

The reason for the broadcast range requirement is to prevent players from
sitting a small ship at the edge of a CP and getting licenses while being
nearly undetectable.

### How Do New Players Get Started?
Each faction is permitted a configurable number of "Unlicensed" grids.  An
Unlicensed hull classifier is required even on these types of grids because of
the broadcast requirement specified above.  These Unlicensed classifiers do
not require and Ship Licenses to build, but they have relatively low block
limits.  They are also limited in the number of them permitted, unlike any
other class.
