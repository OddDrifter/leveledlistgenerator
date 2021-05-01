﻿using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System;
using System.Collections.Immutable;
using System.Linq;
using static MoreLinq.Extensions.BatchExtension;

namespace leveledlistresolver
{
    public class LeveledSpellGraph : MajorRecordGraphBase<ISkyrimMod, ISkyrimModGetter, ILeveledSpell, ILeveledSpellGetter>
    {
        public LeveledSpellGraph(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, in FormKey formKey) : base(state, formKey) { }

        public IObjectBoundsGetter GetObjectBounds()
        {
            return ExtentRecords.Where(record => record.ObjectBounds != Base.ObjectBounds).DefaultIfEmpty(Base).Last().ObjectBounds;
        }

        public byte? GetChanceNone()
        {
            return ExtentRecords.Where(record => record.ChanceNone != Base.ChanceNone).DefaultIfEmpty(Base).Last().ChanceNone;
        }

        public LeveledSpell.Flag GetFlags()
        {
            return ExtentRecords.Where(record => record.Flags != Base.Flags).DefaultIfEmpty(Base).Last().Flags;
        }

        public ImmutableList<ILeveledSpellEntryGetter> GetEntries()
        {
            if (ExtentRecords.Count == 1)
                return (ExtentRecords.Single().Entries ?? Array.Empty<ILeveledSpellEntryGetter>()).ToImmutableList();

            var baseEntries = Base.Entries ?? Array.Empty<ILeveledSpellEntryGetter>();
            var entriesList = ExtentRecords.Select(list => list.Entries ?? Array.Empty<ILeveledSpellEntryGetter>());

            var added = entriesList.Aggregate(ImmutableList.CreateBuilder<ILeveledSpellEntryGetter>(), (builder, items) =>
            {
                builder.AddRange(items.Without(baseEntries).Without(builder));
                return builder;
            }).ToImmutable();

            var intersection = entriesList.Aggregate(ImmutableList.CreateRange(baseEntries), static (list, items) =>
            {
                var toRemove = items.ToList();
                return list.FindAll(toRemove.Remove);
            });

            var items = added.AddRange(intersection).RemoveAll(entry => entry.IsNullOrEmptySublist(linkCache));

            if (items.Count > 255)
            {
                Console.WriteLine($"{GetEditorId()} had more than 255 items.");

                var segments = ((items.Count - 255) / 255) + 1;
                var extraItems = items.RemoveRange(0, 255 - segments);

                var entries = extraItems.Batch(255).WithIndex().Select((kvp) =>
                {
                    var leveledSpell = patchMod.LeveledSpells.AddNew();
                    leveledSpell.EditorID = $"Mir_{GetEditorId()}_Sublist_{kvp.Index + 1}";
                    leveledSpell.Flags = GetFlags();
                    leveledSpell.Entries = kvp.Item.Select(r => r.DeepCopy()).ToExtendedList();

                    return new LeveledSpellEntry()
                    {
                        Data = new() { Reference = leveledSpell.AsLink(), Level = 1, Count = 1 }
                    };
                });

                return items.GetRange(0, 255 - segments).AddRange(entries);
            }

            return items;
        }

        public override LeveledSpell ToMajorRecord()
        {
            var record = Base.DeepCopy();
            record.FormVersion = gameRelease.GetDefaultFormVersion() ?? Base.FormVersion;
            record.ChanceNone = GetChanceNone();
            record.Flags = GetFlags();
            record.Entries = GetEntries().ConvertAll(record => record.DeepCopy()).ToExtendedList();
            return record;
        }
    }
}
