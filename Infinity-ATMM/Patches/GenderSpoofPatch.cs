namespace Infinity_TestMod.Patches
{
    // Gender flip is implemented in TestMod.ToggleGenderSpoof by directly
    // mutating Entity.mainPlayer.Gender (the enum field) — that's the
    // upstream value that GetGenderString() reads from AND that all other
    // direct readers (pronoun helpers, hair option matchers, equip option
    // dicts) consult. Mutating the field flips every consumer with one
    // change. The original value is stashed on activate and restored on
    // deactivate. No Harmony patch needed — this file is intentionally
    // empty so the spoof's location is greppable.
}
