# ACE Loot Generation - Material Name Construction Analysis

## Summary
This document traces the complete flow of how item names are constructed and mutated with material names during loot generation, with a specific focus on the unstable loot naming issue.

---

## Key Issue
**Items show "Bronze Unstable Slashing Hand Axe" in UI but store "Unstable Bronze Slashing Hand Axe" in Name property.**

This suggests a mismatch between the stored Name property and how display methods reconstruct/prepend the material name.

---

## Order of Operations in Mutation Pipeline

### Stage 1: Weenie Creation
**File:** [apps/server/WorldObjects/WorldObject_Properties.cs](apps/server/WorldObjects/WorldObject_Properties.cs)

Initial item created with base Name property (from weenie database).

---

### Stage 2: Item Type-Specific Mutation

#### For Missile Weapons
**File:** [apps/server/Factories/LootGenerationFactory_Missile.cs](apps/server/Factories/LootGenerationFactory_Missile.cs) - Lines 70-119

```csharp
// Line 104-107: Material type is set
MaterialType[] material = [ MaterialType.Ebony, MaterialType.Mahogany, MaterialType.Oak, MaterialType.Pine, MaterialType.Teak ];
var materialType = ThreadSafeRandom.Next(0, 4);
wo.MaterialType = material[materialType];

// Line 119: **CRITICAL LINE** - Sets Name to include material prefix
wo.Name = wo.NameWithMaterialAndElement == null ? wo.Name : wo.NameWithMaterial;
```

**What this does:**
- Calls `GetNameWithMaterial()` which prepends the material name
- **Example transformation:** "Slashing Hand Axe" → "Bronze Slashing Hand Axe"
- This happens BEFORE the unstable naming block

#### For Melee Weapons
**File:** [apps/server/Factories/LootGenerationFactory_Melee.cs](apps/server/Factories/LootGenerationFactory_Melee.cs)

Material is set in mutation but NO explicit assignment to `wo.Name = wo.NameWithMaterial;` observed.

#### For Armor/Clothing
**File:** [apps/server/Factories/LootGenerationFactory_Clothing.cs](apps/server/Factories/LootGenerationFactory_Clothing.cs) - Lines 140+

Material is set but Name is not explicitly mutated with material prepend during this phase.

---

### Stage 3: Unstable Naming Block (if context?.UnstableLoot == true)

**File:** [apps/server/Factories/LootGenerationFactory.cs](apps/server/Factories/LootGenerationFactory.cs) - Lines 2364-2423

**Location:** `CreateRandomLootObjects_New()` method

```csharp
// Line 2374-2380: Check if unstable loot context
if (wo != null && context?.UnstableLoot == true &&
    (wo.ItemType == ItemType.Armor || wo.ItemType == ItemType.MeleeWeapon || 
     wo.ItemType == ItemType.MissileWeapon || wo.ItemType == ItemType.Caster || 
     wo.ItemType == ItemType.Jewelry || wo.ItemType == ItemType.Clothing))
{
    // Line 2383: Flag the item as unstable
    wo.SetProperty(PropertyBool.IsUnstable, true);
    
    // Line 2386: Get the current Name property (for missiles, may already have material)
    var baseName = wo.GetProperty(PropertyString.Name);
    
    // Line 2388-2390: Get material name
    var material = wo.MaterialType != null
        ? RecipeManager.GetMaterialName(wo.MaterialType.Value)
        : null;

    // Lines 2393-2404: Determine element type based on W_DamageType
    var element = "";
    switch (wo.W_DamageType) {
        case DamageType.Fire: element = "Fire"; break;
        case DamageType.Cold: element = "Frost"; break;
        // ... etc
    }

    // Lines 2407-2415: Construct final name with "Unstable" prefix
    string finalName;
    if (!string.IsNullOrEmpty(material)) {
        if (!string.IsNullOrEmpty(element)) {
            finalName = $"Unstable {material} {element} {baseName}";
        } else {
            finalName = $"Unstable {material} {baseName}";
        }
    } else {
        finalName = $"Unstable {baseName}";
    }

    // Line 2422: Set the final name
    wo.SetProperty(PropertyString.Name, finalName);
}
```

**Problem Scenario for Missile Weapons:**
1. **After MutateMissileWeapon:** `wo.Name = "Bronze Slashing Hand Axe"`
2. **Unstable block:** `baseName = "Bronze Slashing Hand Axe"`
3. **Material:** `material = "Bronze"`
4. **Element:** `element = "Slashing"`
5. **Result:** `finalName = "Unstable Bronze Slashing Bronze Slashing Hand Axe"` ❌
   - Material is duplicated ("Bronze" appears twice)
   - Element is duplicated in baseName and prepended

---

## Display Methods That Use Material

### GetNameWithMaterial()
**File:** [apps/server/WorldObjects/WorldObject_Properties.cs](apps/server/WorldObjects/WorldObject_Properties.cs) - Lines 1418-1438

```csharp
public string GetNameWithMaterial(int? stackSize = null)
{
    // Line 1420: If unstable, return Name as-is (no material prepend)
    if (GetProperty(PropertyBool.IsUnstable) == true) {
        return Name;
    }
    
    var name = stackSize != null && stackSize != 1 ? GetPluralName() : Name;

    if (MaterialType == null || WeenieClassId is 1053900) {
        return name;
    }

    var material = RecipeManager.GetMaterialName(MaterialType ?? 0);

    // Line 1432-1435: Remove material if already in name
    if (name.Contains(material)) {
        name = name.Replace(material, "");
    }

    return $"{material} {name}";
}
```

**Logic:**
- ✅ If `IsUnstable == true`: Returns `Name` directly (no prepend)
- ❌ If `IsUnstable == null/false`: Prepends material to name

### GetNameWithMaterialAndElement()
**File:** [apps/server/WorldObjects/WorldObject_Properties.cs](apps/server/WorldObjects/WorldObject_Properties.cs) - Lines 1443-1498

```csharp
public string GetNameWithMaterialAndElement(int? stackSize = null)
{
    // Line 1445: If unstable, return Name as-is
    if (GetProperty(PropertyBool.IsUnstable) == true) {
        return Name;
    }
    
    // ... rest of logic prepends both material and element
    // Line 1494: return $"{material} {name}";
    // Line 1498: return $"{material} {element} {name}";
}
```

**Same logic:** Returns Name as-is if `IsUnstable == true`

---

## Places Where Name Is Modified With Material

### 1. **CRITICAL: MutateMissileWeapon (Line 119)**
**File:** [apps/server/Factories/LootGenerationFactory_Missile.cs](apps/server/Factories/LootGenerationFactory_Missile.cs#L119)

```csharp
wo.Name = wo.NameWithMaterialAndElement == null ? wo.Name : wo.NameWithMaterial;
```

- **Impact:** HIGH - Prepends material before unstable naming block runs
- **Timing:** EARLY in mutation pipeline
- **Side Effect:** baseName in unstable block may already contain material

---

### 2. MutateTrophy (Trophy Prefix)
**File:** [apps/server/Factories/LootGenerationFactory.cs](apps/server/Factories/LootGenerationFactory.cs#L3028)

```csharp
wo.SetProperty(PropertyString.Name, name + " " + wo.Name);
```

- Prepends quality name to trophy Name
- Separate from material handling

---

### 3. MutateDinnerware (Gem-Related)
**File:** [apps/server/Factories/LootGenerationFactory_Dinnerware.cs](apps/server/Factories/LootGenerationFactory_Dinnerware.cs)

- Sets `wo.MaterialType` but doesn't explicitly modify Name

---

### 4. MutateCaster (Element Replacement)
**File:** [apps/server/Factories/LootGenerationFactory_Caster.cs](apps/server/Factories/LootGenerationFactory_Caster.cs#L182-208)

```csharp
wo.Name = wo.Name.Replace("Slashing", "Life");
wo.Name = wo.Name.Replace("Blunt", "Life");
// ... etc
```

- Replaces damage type with "Life" but doesn't prepend material

---

### 5. MutateMissileElement (Element Prefix)
**File:** [apps/server/Factories/LootGenerationFactory_Missile.cs](apps/server/Factories/LootGenerationFactory_Missile.cs#L643)

```csharp
wo.Name = elementString + " " + wo.Name;
```

- Prepends element type to name
- May interact with unstable naming logic

---

## UI Display Path

When items are displayed in UI/Discord/Market:

**File:** [apps/server/Discord/MarketListingFormatter.cs](apps/server/Discord/MarketListingFormatter.cs#L1105-1120)

```csharp
if (obj.ItemType == ItemType.TinkeringMaterial) {
    cache.Name = obj.NameWithMaterial;  // Uses GetNameWithMaterial()
    return cache.Name;
}
if (!string.IsNullOrWhiteSpace(obj.Name)) {
    cache.Name = obj.Name;  // Returns stored Name property
    return cache.Name;
}
```

- For salvage materials: calls `GetNameWithMaterial()`
- For other items: returns stored `Name` property directly

---

## Root Cause Analysis

### The Problem
For **unstable missile weapons**, the order of operations creates a potential issue:

1. **MutateMissileWeapon** (Stage 2) prepends material:
   - Input: `"Slashing Hand Axe"`
   - Output: `Name = "Bronze Slashing Hand Axe"`

2. **Unstable Naming Block** (Stage 3) tries to construct unstable name:
   - Gets: `baseName = "Bronze Slashing Hand Axe"` (already has material!)
   - Gets: `material = "Bronze"` (duplicates from baseName)
   - Gets: `element = "Slashing"` (also in baseName)
   - Creates: `"Unstable Bronze Slashing Bronze Slashing Hand Axe"` ❌

### Missing Cleanup
The unstable naming block should **remove** the material from baseName before constructing the final name, similar to what `GetNameWithMaterial()` does:

```csharp
if (name.Contains(material)) {
    name = name.Replace(material, "");
}
```

---

## Solution Recommendations

### For Missile Weapons
**File:** [apps/server/Factories/LootGenerationFactory_Missile.cs](apps/server/Factories/LootGenerationFactory_Missile.cs#L119)

**Option 1: Remove the early material prepend**
```csharp
// REMOVE this line:
// wo.Name = wo.NameWithMaterialAndElement == null ? wo.Name : wo.NameWithMaterial;

// Let the unstable naming block handle it instead
```

**Option 2: Clean up material in unstable naming block**
**File:** [apps/server/Factories/LootGenerationFactory.cs](apps/server/Factories/LootGenerationFactory.cs#L2386)

```csharp
var baseName = wo.GetProperty(PropertyString.Name);

// Remove material from baseName if it's already there
if (!string.IsNullOrEmpty(material) && baseName.Contains(material)) {
    baseName = baseName.Replace(material, "").Trim();
}
```

---

## Files to Review

| File | Lines | Purpose |
|------|-------|---------|
| [LootGenerationFactory.cs](apps/server/Factories/LootGenerationFactory.cs) | 2364-2423 | Unstable naming block |
| [LootGenerationFactory_Missile.cs](apps/server/Factories/LootGenerationFactory_Missile.cs) | 70-150 | Missile weapon mutation |
| [LootGenerationFactory_Melee.cs](apps/server/Factories/LootGenerationFactory_Melee.cs) | 1-250 | Melee weapon mutation |
| [LootGenerationFactory_Caster.cs](apps/server/Factories/LootGenerationFactory_Caster.cs) | 180-210 | Caster element handling |
| [WorldObject_Properties.cs](apps/server/WorldObjects/WorldObject_Properties.cs) | 1410-1510 | Name and NameWithMaterial properties |
| [MarketListingFormatter.cs](apps/server/Discord/MarketListingFormatter.cs) | 1090-1170 | UI name display |

---

## Mutation Pipeline Summary

```
CreateRandomLootObjects_New()
  └─> CreateAndMutateWcid()
       └─> MutateMissileWeapon() [STAGE 2]
            ├─ SetMaterialType()
            └─ wo.Name = wo.NameWithMaterial  ← PREPENDS MATERIAL
       └─> (return to CreateRandomLootObjects_New)
  └─> Unstable naming block [STAGE 3]
       ├─ baseName = wo.GetProperty(PropertyString.Name)  ← ALREADY HAS MATERIAL
       ├─ material = RecipeManager.GetMaterialName(wo.MaterialType)
       ├─ Construct: $"Unstable {material} {element} {baseName}"
       └─ wo.SetProperty(PropertyString.Name, finalName)
```

**Issue:** baseName at Stage 3 may already include the material from Stage 2, causing duplication.
