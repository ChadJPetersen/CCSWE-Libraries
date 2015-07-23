using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace CCSWE.Collections.ObjectModel
{
    /// <summary>
    /// the event that handles adding new items to the collection
    /// </summary>
    /// <param name="source">The source of the event.</param>
    /// <param name="e">The event arguments object.</param>
    public delegate void addIntoEventHandler(object source, AddIntoEvent e);

    //TODO: SynchronizedObservableCollection<T> - Add xmldoc
    //TODO: SynchronizedObservableCollection<T> - ObservableCollection<T>.Move() is not implemented...
    [Serializable]
    [ComVisible(false)]
    [DebuggerDisplay("Count = {Count}")]
    public class SynchronizedObservableCollection<T> : IDisposable, IList<T>, IList, IReadOnlyList<T>, INotifyCollectionChanged, INotifyPropertyChanged
    {
        public event addIntoEventHandler addedToEvent;
        #region Private Fields

        private readonly SynchronizationContext _context;

        private readonly IList<T> _items = new List<T>();

        private readonly ReaderWriterLockSlim _itemsLocker = new ReaderWriterLockSlim();

        private readonly SimpleMonitor _monitor = new SimpleMonitor();

        [NonSerialized]
        private Object _syncRoot;

        #endregion Private Fields

        #region Public Constructors

        public SynchronizedObservableCollection()
        {
            _context = SynchronizationContext.Current;
        }

        public SynchronizedObservableCollection(IEnumerable<T> collection)
            : this()
        {
            if (collection == null)
            {
                throw new ArgumentNullException("collection", "'collection' cannot be null");
            }

            foreach (var item in collection)
            {
                _items.Add(item);
            }
        }

        public SynchronizedObservableCollection(SynchronizationContext context)
        {
            _context = context;
        }

        public SynchronizedObservableCollection(IEnumerable<T> collection, SynchronizationContext context)
            : this(context)
        {
            if (collection == null)
            {
                throw new ArgumentNullException("collection", "'collection' cannot be null");
            }

            foreach (var item in collection)
            {
                _items.Add(item);
            }
        }

        #endregion Public Constructors

        #region Public Events

        public event NotifyCollectionChangedEventHandler CollectionChanged;

        event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
        {
            add { PropertyChanged += value; }
            remove { PropertyChanged -= value; }
        }

        #endregion Public Events

        #region Protected Events

        protected event PropertyChangedEventHandler PropertyChanged;

        #endregion Protected Events

        #region Public Properties

        public int Count
        {
            get
            {
                _itemsLocker.EnterReadLock();

                try
                {
                    return _items.Count;
                }
                finally
                {
                    _itemsLocker.ExitReadLock();
                }
            }
        }

        bool ICollection.IsSynchronized
        {
            get { return true; }
        }

        object ICollection.SyncRoot
        {
            get
            {
                if (_syncRoot == null)
                {
                    _itemsLocker.EnterReadLock();

                    try
                    {
                        var c = _items as ICollection;
                        if (c != null)
                        {
                            _syncRoot = c.SyncRoot;
                        }
                        else
                        {
                            Interlocked.CompareExchange<Object>(ref _syncRoot, new Object(), null);
                        }
                    }
                    finally
                    {
                        _itemsLocker.ExitReadLock();
                    }
                }

                return _syncRoot;
            }
        }

        bool ICollection<T>.IsReadOnly
        {
            get { return _items.IsReadOnly; }
        }

        bool IList.IsFixedSize
        {
            get
            {
                var list = _items as IList;
                if (list != null)
                {
                    return list.IsFixedSize;
                }

                return _items.IsReadOnly;
            }
        }

        bool IList.IsReadOnly
        {
            get { return _items.IsReadOnly; }
        }

        #endregion Public Properties

        #region Public Indexers

        object IList.this[int index]
        {
            get { return this[index]; }
            set
            {
                try
                {
                    this[index] = (T)value;
                }
                catch (InvalidCastException)
                {
                    throw new ArgumentException("'value' is the wrong type");
                }
            }
        }

        public T this[int index]
        {
            get
            {
                _itemsLocker.EnterReadLock();

                try
                {
                    CheckIndex(index);

                    return _items[index];
                }
                finally
                {
                    _itemsLocker.ExitReadLock();
                }
            }
            set
            {
                T oldValue;

                _itemsLocker.EnterWriteLock();

                try
                {
                    CheckIsReadOnly();
                    CheckIndex(index);
                    CheckReentrancy();

                    oldValue = this[index];

                    _items[index] = value;
                }
                finally
                {
                    _itemsLocker.ExitWriteLock();
                }

                OnPropertyChanged("Item[]");
                OnCollectionChanged(NotifyCollectionChangedAction.Replace, oldValue, value, index);
            }
        }

        #endregion Public Indexers

        #region Public Methods

        public void Add(T item)
        {
            _itemsLocker.EnterWriteLock();

            var index = -1;

            try
            {
                CheckIsReadOnly();
                CheckReentrancy();

                index = _items.Count;

                _items.Insert(index, item);
            }
            finally
            {
                _itemsLocker.ExitWriteLock();
            }

            OnPropertyChanged("Count");
            OnPropertyChanged("Item[]");
            OnCollectionChanged(NotifyCollectionChangedAction.Add, item, index);
        }

        public void Clear()
        {
            _itemsLocker.EnterWriteLock();

            try
            {
                CheckIsReadOnly();
                CheckReentrancy();

                _items.Clear();
            }
            finally
            {
                _itemsLocker.ExitWriteLock();
            }

            OnPropertyChanged("Count");
            OnPropertyChanged("Item[]");
            OnCollectionReset();
        }

        public bool Contains(T item)
        {
            _itemsLocker.EnterReadLock();

            try
            {
                return _items.Contains(item);
            }
            finally
            {
                _itemsLocker.ExitReadLock();
            }
        }

        public void CopyTo(T[] array, int index)
        {
            _itemsLocker.EnterReadLock();

            try
            {
                _items.CopyTo(array, index);
                if (addedToEvent != null)
                {
                    addedToEvent(this, new AddIntoEvent(index, array.Length));
                }
            }
            finally
            {
                _itemsLocker.ExitReadLock();
            }
        }

        public void Dispose()
        {
            _itemsLocker.Dispose();
        }

        public SynchronizedObservableCollectionEnumerator<T> GetEnumerator()
        {
            _itemsLocker.EnterReadLock();

            try
            {
                return new SynchronizedObservableCollectionEnumerator<T>(this);
            }
            finally
            {
                _itemsLocker.ExitReadLock();
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            _itemsLocker.EnterReadLock();

            try
            {
                if (array == null)
                {
                    throw new ArgumentNullException("array", "'array' cannot be null");
                }

                if (array.Rank != 1)
                {
                    throw new ArgumentException("Multi-dimension arrays are not supported", "array");
                }

                if (array.GetLowerBound(0) != 0)
                {
                    throw new ArgumentException("Non-zero lower bound arrays are not supported", "array");
                }

                if (index < 0)
                {
                    throw new ArgumentOutOfRangeException("index", "'index' is out of range");
                }

                if (array.Length - index < _items.Count)
                {
                    throw new ArgumentException("Array is too small");
                }

                var tArray = array as T[];
                if (tArray != null)
                {
                    _items.CopyTo(tArray, index);
                    if (addedToEvent != null)
                    {
                        addedToEvent(this, new AddIntoEvent(index, array.Length));
                    }
                }
                else
                {
                    //
                    // Catch the obvious case assignment will fail.
                    // We can found all possible problems by doing the check though.
                    // For example, if the element type of the Array is derived from T,
                    // we can't figure out if we can successfully copy the element beforehand.
                    //
                    var targetType = array.GetType().GetElementType();
                    var sourceType = typeof(T);
                    if (!(targetType.IsAssignableFrom(sourceType) || sourceType.IsAssignableFrom(targetType)))
                    {
                        throw new ArrayTypeMismatchException("Invalid array type");
                    }

                    //
                    // We can't cast array of value type to object[], so we don't support
                    // widening of primitive types here.
                    //
                    var objects = array as object[];
                    if (objects == null)
                    {
                        throw new ArrayTypeMismatchException("Invalid array type");
                    }

                    var count = _items.Count;
                    try
                    {
                        for (var i = 0; i < count; i++)
                        {
                            objects[index++] = _items[i];
                        }
                    }
                    catch (ArrayTypeMismatchException)
                    {
                        throw new ArrayTypeMismatchException("Invalid array type");
                    }
                }
            }
            finally
            {
                _itemsLocker.ExitReadLock();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            _itemsLocker.EnterReadLock();

            try
            {
                return (IEnumerator)new SynchronizedObservableCollectionEnumerator<T>(this);
            }
            finally
            {
                _itemsLocker.ExitReadLock();
            }
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return (IEnumerator<T>)GetEnumerator();
        }

        int IList.Add(object value)
        {
            _itemsLocker.EnterWriteLock();

            var index = -1;
            T item;

            try
            {
                CheckIsReadOnly();
                CheckReentrancy();

                index = _items.Count;
                item = (T)value;

                _items.Insert(index, item);
            }
            catch (InvalidCastException)
            {
                throw new ArgumentException("'value' is the wrong type");
            }
            finally
            {
                _itemsLocker.ExitWriteLock();
            }

            OnPropertyChanged("Count");
            OnPropertyChanged("Item[]");
            OnCollectionChanged(NotifyCollectionChangedAction.Add, item, index);

            return index;
        }

        bool IList.Contains(object value)
        {
            if (IsCompatibleObject(value))
            {
                _itemsLocker.EnterReadLock();

                try
                {
                    return _items.Contains((T)value);
                }
                finally
                {
                    _itemsLocker.ExitReadLock();
                }
            }

            return false;
        }

        int IList.IndexOf(object value)
        {
            if (IsCompatibleObject(value))
            {
                _itemsLocker.EnterReadLock();

                try
                {
                    return _items.IndexOf((T)value);
                }
                finally
                {
                    _itemsLocker.ExitReadLock();
                }
            }

            return -1;
        }

        void IList.Insert(int index, object value)
        {
            try
            {
                Insert(index, (T)value);
                if (addedToEvent != null)
                {
                    addedToEvent(this, new AddIntoEvent(index, 1));
                }
            }
            catch (InvalidCastException)
            {
                throw new ArgumentException("'value' is the wrong type");
            }
        }

        void IList.Remove(object value)
        {
            if (IsCompatibleObject(value))
            {
                Remove((T)value);
            }
        }

        public int IndexOf(T item)
        {
            _itemsLocker.EnterReadLock();

            try
            {
                return _items.IndexOf(item);
            }
            finally
            {
                _itemsLocker.ExitReadLock();
            }
        }

        public void Insert(int index, T item)
        {
            _itemsLocker.EnterWriteLock();

            try
            {
                CheckIsReadOnly();
                CheckIndex(index);
                CheckReentrancy();

                _items.Insert(index, item);
                if (addedToEvent != null)
                {
                    addedToEvent(this, new AddIntoEvent(index, 1));
                }
            }
            finally
            {
                _itemsLocker.ExitWriteLock();
            }

            OnPropertyChanged("Count");
            OnPropertyChanged("Item[]");
            OnCollectionChanged(NotifyCollectionChangedAction.Add, item, index);
        }

        public bool Remove(T item)
        {
            int index;
            T value;

            _itemsLocker.EnterWriteLock();

            try
            {
                CheckIsReadOnly();
                CheckReentrancy();

                index = _items.IndexOf(item);

                if (index < 0)
                {
                    return false;
                }

                value = _items[index];

                _items.RemoveAt(index);
                if (addedToEvent != null)
                {
                    addedToEvent(this, new AddIntoEvent(index, -1));
                }
            }
            finally
            {
                _itemsLocker.ExitWriteLock();
            }

            OnPropertyChanged("Count");
            OnPropertyChanged("Item[]");
            OnCollectionChanged(NotifyCollectionChangedAction.Remove, value, index);

            return true;
        }

        public void RemoveAt(int index)
        {
            T value;

            _itemsLocker.EnterWriteLock();

            try
            {
                CheckIsReadOnly();
                CheckIndex(index);
                CheckReentrancy();

                value = _items[index];

                _items.RemoveAt(index);
                if (addedToEvent != null)
                {
                    addedToEvent(this, new AddIntoEvent(index, -1));
                }
            }
            finally
            {
                _itemsLocker.ExitWriteLock();
            }

            OnPropertyChanged("Count");
            OnPropertyChanged("Item[]");
            OnCollectionChanged(NotifyCollectionChangedAction.Remove, value, index);
        }

        #endregion Public Methods

        #region Private Methods

        private static bool IsCompatibleObject(object value)
        {
            // Non-null values are fine.  Only accept nulls if T is a class or Nullable<U>.
            // Note that default(T) is not equal to null for value types except when T is Nullable<U>.
            return ((value is T) || (value == null && default(T) == null));
        }

        private IDisposable BlockReentrancy()
        {
            _monitor.Enter();

            return _monitor;
        }

        // ReSharper disable once UnusedParameter.Local
        private void CheckIndex(int index)
        {
            if (index < 0 || index >= _items.Count)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        private void CheckIsReadOnly()
        {
            if (_items.IsReadOnly)
            {
                throw new NotSupportedException("Collection is read-only");
            }
        }

        private void CheckReentrancy()
        {
            if (_monitor.Busy && CollectionChanged != null && CollectionChanged.GetInvocationList().Length > 1)
            {
                throw new InvalidOperationException("SynchronizedObservableCollection reentrancy not allowed");
            }
        }

        private void OnCollectionChanged(NotifyCollectionChangedAction action, object item, int index)
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(action, item, index));
        }

        private void OnCollectionChanged(NotifyCollectionChangedAction action, object item, int index, int oldIndex)
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(action, item, index, oldIndex));
        }

        private void OnCollectionChanged(NotifyCollectionChangedAction action, object oldItem, object newItem, int index)
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(action, newItem, oldItem, index));
        }

        private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            var collectionChanged = CollectionChanged;
            if (collectionChanged == null)
            {
                return;
            }

            using (BlockReentrancy())
            {
                _context.Send(state => collectionChanged(this, e), null);
            }
        }

        private void OnCollectionReset()
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        private void OnPropertyChanged(string propertyName)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
        }

        private void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            var propertyChanged = PropertyChanged;
            if (propertyChanged == null)
            {
                return;
            }

            _context.Send(state => propertyChanged(this, e), null);
        }

        #endregion Private Methods

        #region Private Classes

        private class SimpleMonitor : IDisposable
        {
            #region Private Fields

            private int _busyCount;

            #endregion Private Fields

            #region Public Properties

            public bool Busy
            {
                get { return _busyCount > 0; }
            }

            #endregion Public Properties

            #region Public Methods

            public void Dispose()
            {
                --_busyCount;
            }

            public void Enter()
            {
                ++_busyCount;
            }

            #endregion Public Methods
        }

        #endregion Private Classes
    }

    /// <summary>
    /// Class that enumerates over the <see cref="SynchronizedObservableCollection"/> class.
    /// Forward only enumeration guarantied to not output duplicates if items are added before current location.
    /// </summary>
    /// <typeparam name="T">The type that the collection is holding.</typeparam>
    public class SynchronizedObservableCollectionEnumerator<T> : IEnumerator<T>, IEnumerator
    {
        private ReaderWriterLockSlim _positionLock = new ReaderWriterLockSlim();
        private addIntoEventHandler eHandler;
        public void collectionChanged(object source, AddIntoEvent e)
        {
            _positionLock.EnterWriteLock();
            try
            {
                if (e.startIndex < position)
                {
                    position += e.numberAdded;
                }
            }
            finally
            {
                _positionLock.ExitWriteLock();
            }
        }
        #region Public Fields

        /// <summary>
        /// The collection that is being enumerated
        /// </summary>
        public SynchronizedObservableCollection<T> syncedObsCol;

        #endregion Public Fields

        #region Private Fields

        /// <summary>
        /// weather this enumerator is already disposed
        /// </summary>
        private bool disposed = false;

        /// <summary>
        /// The position of the enumeration
        /// </summary>
        private int position = -1;

        #endregion Private Fields

        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SynchronizedObservableCollectionEnumerator{T}"/> class.
        /// </summary>
        /// <param name="cols">The collection to enumerate over.</param>
        public SynchronizedObservableCollectionEnumerator(SynchronizedObservableCollection<T> cols)
        {
            syncedObsCol = cols;
            this.eHandler = new addIntoEventHandler(collectionChanged);
            cols.addedToEvent += this.eHandler;
        }

        #endregion Public Constructors

        #region Public Properties

        /// <summary>
        /// Gets the element in the collection at the current position of the enumerator.
        /// </summary>
        public T Current
        {
            get
            {
                return getCurrent();
            }
        }

        /// <summary>
        /// Gets the element in the collection at the current position of the enumerator.
        /// </summary>
        object IEnumerator.Current
        {
            get { return getCurrent(); }
        }

        #endregion Public Properties

        #region Public Methods

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Advances the enumerator to the next element of the collection.
        /// </summary>
        /// <returns>
        /// true if the enumerator was successfully advanced to the next element; false if the enumerator has passed the end of the collection.
        /// </returns>
        public bool MoveNext()
        {
            _positionLock.EnterWriteLock();
            try
            {
                position++;
                return position < syncedObsCol.Count;
            }
            finally
            {
                _positionLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Sets the enumerator to its initial position, which is before the first element in the collection.
        /// </summary>
        public void Reset()
        {
            _positionLock.EnterWriteLock();
            try
            {
                position = -1;
            }
            finally
            {
                _positionLock.ExitWriteLock();
            }
        }

        #endregion Public Methods

        #region Protected Methods

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    //set the pointer to null so that the GC can get it. we
                    //don't want actually dispose of the object in case it is
                    //still being used elsewhere.
                    syncedObsCol.addedToEvent -= this.eHandler;
                    syncedObsCol = null;
                }
            }
            this.disposed = true;
        }

        #endregion Protected Methods

        #region Private Methods

        /// <summary>
        /// Gets the current value.
        /// </summary>
        /// <returns>The current value.</returns>
        private T getCurrent()
        {
            _positionLock.EnterReadLock();
            try
            {
                return syncedObsCol.ElementAt(position);
            }
            finally
            {
                _positionLock.ExitReadLock();
            }
        }

        #endregion Private Methods
    }

    public class AddIntoEvent : EventArgs
    {
        public AddIntoEvent(int startIndex, int numberAdded)
        {
            this.startIndex = startIndex;
            this.numberAdded = numberAdded;
        }
        /// <summary>
        /// The start index storage object
        /// </summary>
        private int _startIndex;
        /// <summary>
        /// The number added object
        /// </summary>
        private int _numberAdded;
        /// <summary>
        /// Gets or sets the start index.
        /// </summary>
        /// <value>
        /// The start index.
        /// </value>
        public int startIndex
        {
            get
            {
                return _startIndex;
            }
            set
            {
                _startIndex = value;
            }
        }

        /// <summary>
        /// Gets or sets the number added.
        /// </summary>
        /// <value>
        /// The number added.
        /// </value>
        public int numberAdded
        {
            get
            {
                return _numberAdded;
            }
            set
            {
                _numberAdded = value;
            }
        }
    }
}