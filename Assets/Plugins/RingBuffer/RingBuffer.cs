using System;
using System.Collections;
using System.Collections.Generic;

namespace RingBuffer {
  public class RingBuffer<T> : IEnumerable<T> {

    public struct Iterator {
      Int32 _count;
      Int32 _index;

      readonly Int32 _version;
      readonly RingBuffer<T> _buffer;

      public Iterator(RingBuffer<T> buffer) {

        _buffer = buffer;
        _version = buffer._version;

        _index = 0;
        _count = buffer._count;
      }

      public Boolean Next(out T item) {
        if (_version != _buffer._version) {
          throw new InvalidOperationException("RingBuffer has been modified");
        }

        if (_index < _count) {
          // assign item
          item = _buffer[_index];

          // step up index
          _index += 1;
          return true;
        }

        item = default(T);
        return false;
      }
    }

    Int32 _head;
    Int32 _tail;
    Int32 _count;
    Int32 _version;

    readonly T[] _array;
    readonly Boolean _overwrite;

    public Int32 Count {
      get {
        return _count;
      }
    }

    public Int32 Capacity {
      get {
        return _array.Length;
      }
    }

    public Boolean IsFull {
      get {
        return _count == _array.Length;
      }
    }

    public Boolean IsEmpty {
      get {
        return _count == 0;
      }
    }

    public T this[Int32 index] {
      get {
        if (index < 0 || index >= _count) {
          throw new IndexOutOfRangeException();
        }

        return _array[(_tail + index) % _array.Length];
      }
      set {
        if (index < 0 || index >= _count) {
          throw new IndexOutOfRangeException();
        }

        _array[(_tail + index) % _array.Length] = value;
      }
    }

    public Iterator GetIterator() {
      return new Iterator(this);
    }

    public RingBuffer(Int32 size, Boolean overwrite) {
      _array = new T[size];
      _overwrite = overwrite;
    }

    public void Push(T item) {
      if (IsFull) {
        if (_overwrite) {
          Pop();
        }
        else {
          throw new InvalidOperationException();
        }
      }

      //
      _version += 1;

      // store value at head
      _array[_head] = item;

      // move head pointer forward
      _head = (_head + 1) % _array.Length;

      // add count
      _count += 1;

    }

    public T Pop() {
      if (IsEmpty) {
        throw new InvalidOperationException();
      }

      //
      _version += 1;

      // copy item from tail
      var item = _array[_tail];

      // clear item and move tail forward
      _array[_tail] = default(T);
      _tail = (_tail + 1) % _array.Length;

      // reduce count
      _count -= 1;
      
      return item;
    }

    public void Clear() {
      _head = 0;
      _tail = 0;
      _count = 0;
      _version += 1;

      Array.Clear(_array, 0, _array.Length);
    }

    public IEnumerator<T> GetEnumerator() {
      for (Int32 i = 0; i < _count; ++i) {
        yield return this[i];
      }
    }

    IEnumerator IEnumerable.GetEnumerator() {
      return GetEnumerator();
    }
  }
}