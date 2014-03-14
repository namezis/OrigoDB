﻿using System;
using System.Collections.Generic;
using System.Linq;
using OrigoDB.Core.Storage;
using System.IO;

namespace OrigoDB.Core
{

    /// <summary>
    /// For testing of OrigoDB Core without writing to disk
    /// </summary>
    public class InMemoryStore : Store
    {
        private class InMemoryStoreState
        {
            public readonly Dictionary<Snapshot, byte[]> Snapshots;
            public readonly List<MemoryStream> Journal;

            public InMemoryStoreState()
            {
                Snapshots = new Dictionary<Snapshot, byte[]>();
                Journal = new List<MemoryStream>();
            }
        }

        static readonly Dictionary<string, InMemoryStoreState> _states
            = new Dictionary<string, InMemoryStoreState>();

        readonly InMemoryStoreState _state;

        public InMemoryStore()
            : this(Guid.NewGuid().ToString())
        {

        }

        public InMemoryStore(string clientIdentifier)
            : this(new EngineConfiguration(clientIdentifier))
        {
            
        }

        public InMemoryStore(EngineConfiguration config)
            : base(config)
        {

            string key = _config.Location.OfJournal;
            if (!_states.ContainsKey(key)) _states.Add(key, new InMemoryStoreState());
            _state = _states[key];
        }



        protected override IJournalWriter CreateStoreSpecificJournalWriter(ulong lastEntryId)
        {
            return new StreamJournalWriter(this, _config);
        }

        protected override Snapshot WriteSnapshotImpl(Model model)
        {
            var bytes = _serializer.Serialize(model);
            var snapshot = new Snapshot(DateTime.Now, LastEntryId);
            _state.Snapshots.Add(snapshot, bytes);
            return snapshot;
        }

        public override IEnumerable<JournalEntry> GetJournalEntriesFrom(ulong entryId)
        {
            return _state.Journal.SelectMany(
                journalSegment => _serializer.ReadToEnd<JournalEntry>(new MemoryStream(journalSegment.ToArray())).SkipWhile(e => e.Id < entryId));
        }

        public override IEnumerable<JournalEntry> GetJournalEntriesBeforeOrAt(DateTime pointInTime)
        {
            return GetJournalEntriesFrom(0).TakeWhile(entry => entry.Created <= pointInTime);
        }

        public override Model LoadMostRecentSnapshot(out ulong lastEntryId)
        {

            lastEntryId = 0;
            Model result = null;

            var key = _state.Snapshots.OrderByDescending(s => s.Key.Created).Select(kvp => kvp.Key).FirstOrDefault();

            if (key != null)
            {
                lastEntryId = key.LastEntryId;
                result = _serializer.Deserialize<Model>(_state.Snapshots[key]);
            }
            return result;
        }

        public override void VerifyCanLoad()
        {
            if (_state.Snapshots.Count == 0) throw new InvalidOperationException();
        }

        public override void VerifyCanCreate()
        {
            if (_state.Snapshots.Any()) throw new InvalidOperationException();
        }

        public override void Create(Model model)
        {
            WriteSnapshotImpl(model);
        }

        protected override IEnumerable<Snapshot> LoadSnapshots()
        {
            foreach (var snapshot in _state.Snapshots.Keys.OrderBy(ss => ss.Created)) yield return snapshot;
        }

        public override Stream CreateJournalWriterStream(ulong firstEntryId = 1)
        {
            var stream = new MemoryStream();
            _state.Journal.Add(stream);
            return stream;
        }
    }
}
