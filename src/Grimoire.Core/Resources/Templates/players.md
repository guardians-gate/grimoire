# {{name}}

| Property           | Value                                                         |
|--------------------|---------------------------------------------------------------|
| Class              | {{class}}{{ddb.character.classes[0].definition.name}}         |
| Level              | {{level::ordinal}}{{ddb.character.classes[0].level::ordinal}} |
| Species            | {{species}}{{ddb.character.race.fullName}}                    |
| Background         | {{background}}{{ddb.character.background.definition.name}}    |
| Alignment          | {{alignment}}{{ddb.character.alignmentId}}                    |
| Proficiency Bonus  | {{proficiencyBonus}}                                          |
| D&D Beyond ID      | {{_dndBeyondId::ddb-link}}                                    |
| Armor Class        | {{armorClass}}{{ddb.character.armorClass}}                    |
| Hit Points         | {{hitPoints}}{{ddb.character.baseHitPoints}}                  |
| Speed              | {{speed::unit:ft.}}                                           |
| Initiative         | {{initiative}}                                                |
| Passive Perception | {{passivePerception}}                                         |
| Spell Save DC      | {{spellSaveDc}}                                               |
| Spell Attack Bonus | {{spellAttackBonus}}                                          |
| Tags               | {{tags::csv}}                                                 |

{{description}}

## Ability Scores
| Ability | Score            |
|---------|------------------|
| STR     | {{strength}}     |
| DEX     | {{dexterity}}    |
| CON     | {{constitution}} |
| INT     | {{intelligence}} |
| WIS     | {{wisdom}}       |
| CHA     | {{charisma}}     |

## Race
### {{ddb.character.race.fullName}}
{{ddb.character.race.description}}

{{#each ddb.character.race.racialTraits}}
- **{{definition.name}}:** {{definition.description}}
{{/each}}

## Background
### {{ddb.character.background.definition.name}}
{{ddb.character.background.definition.description}}

### {{ddb.character.background.customBackground.name}}
{{ddb.character.background.customBackground.featuresBackground.featureDescription}}

## Classes
{{#each ddb.character.classes}}
### {{definition.name}} ({{level::ordinal}} Level)
**Subclass:** {{subclassDefinition.name}}

{{#each classFeatures}}
- **{{definition.name}}:** {{definition.description}}
{{/each}}
{{/each}}

## Feats
{{#each ddb.character.feats}}
- **{{definition.name}}:** {{definition.description}}
{{/each}}

## Spells
{{#each ddb.character.spells.class}}
- **{{definition.name}}** ({{definition.level::ordinal}}): {{definition.snippet}}
{{/each}}
{{#each ddb.character.spells.race}}
- **{{definition.name}}** ({{definition.level::ordinal}}): {{definition.snippet}}
{{/each}}
{{#each ddb.character.spells.feat}}
- **{{definition.name}}** ({{definition.level::ordinal}}): {{definition.snippet}}
{{/each}}
{{#each ddb.character.spells.background}}
- **{{definition.name}}** ({{definition.level::ordinal}}): {{definition.snippet}}
{{/each}}
{{#each ddb.character.spells.item}}
- **{{definition.name}}** ({{definition.level::ordinal}}): {{definition.snippet}}
{{/each}}

## Actions
{{#each ddb.character.actions.class}}
- **{{name}}** ({{actionType}}): {{description}}
{{/each}}
{{#each ddb.character.actions.race}}
- **{{name}}** ({{actionType}}): {{description}}
{{/each}}
{{#each ddb.character.actions.feat}}
- **{{name}}** ({{actionType}}): {{description}}
{{/each}}
{{#each ddb.character.actions.background}}
- **{{name}}** ({{actionType}}): {{description}}
{{/each}}
{{#each ddb.character.actions.item}}
- **{{name}}** ({{actionType}}): {{description}}
{{/each}}

## Options
{{#each ddb.character.options.class}}
- **{{definition.name}}:** {{definition.description}}
{{/each}}
{{#each ddb.character.options.race}}
- **{{definition.name}}:** {{definition.description}}
{{/each}}
{{#each ddb.character.options.background}}
- **{{definition.name}}:** {{definition.description}}
{{/each}}
{{#each ddb.character.options.feat}}
- **{{definition.name}}:** {{definition.description}}
{{/each}}

## Character Sheet
{{characterSheet}}
