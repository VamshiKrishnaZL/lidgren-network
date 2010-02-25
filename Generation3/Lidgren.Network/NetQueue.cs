﻿using System;

namespace Lidgren.Network
{
	/// <summary>
	/// Thread safe queue with TryDequeue()
	/// </summary>
	public sealed class NetQueue<T>
	{
		// Example:
		// m_capacity = 8
		// m_size = 6
		// m_head = 4
		//
		// [0] item
		// [1] item (tail = ((head + size - 1) % capacity)
		// [2] 
		// [3] 
		// [4] item (head)
		// [5] item
		// [6] item 
		// [7] item
		//
		private T[] m_items;
		private object m_lock;
		private int m_size;
		private int m_head;

		public int Count { get { return m_size; } }

		public NetQueue(int initialCapacity)
		{
			m_lock = new object();
			m_items = new T[initialCapacity];
		}

		public void Enqueue(T item)
		{
			if (m_size == m_items.Length)
				SetCapacity(m_items.Length + 8);

			lock (m_lock)
			{
				int slot = (m_head + m_size) % m_items.Length;
				m_items[slot] = item;
				m_size++;
			}
		}

		private void SetCapacity(int newCapacity)
		{
			if (m_size == 0)
			{
				lock (m_lock)
				{
					if (m_size == 0)
					{
						m_items = new T[newCapacity];
						m_head = 0;
						return;
					}
				}
			}

			T[] newItems = new T[newCapacity];

			lock (m_lock)
			{
				if (m_head + m_size - 1 < m_items.Length)
				{
					Array.Copy(m_items, m_head, newItems, 0, m_size);
				}
				else
				{
					Array.Copy(m_items, m_head, newItems, 0, m_items.Length - m_head);
					Array.Copy(m_items, 0, newItems, m_items.Length - m_head, (m_size - (m_items.Length - m_head)));
				}

				m_items = newItems;
				m_head = 0;
			}
		}

		public T TryDequeue()
		{
			if (m_size == 0)
				return default(T);

			lock (m_lock)
			{
				if (m_size == 0)
					return default(T);

				T retval = m_items[m_head];
				m_items[m_head] = default(T);

				m_head = (m_head + 1) % m_items.Length;
				m_size--;

				return retval;
			}
		}

		public bool Contains(T item)
		{
			lock (m_lock)
			{
				int ptr = m_head;
				for (int i = 0; i < m_size; i++)
				{
					if (m_items[ptr].Equals(item))
						return true;
					ptr = (ptr + 1) % m_items.Length;
				}
			}
			return false;
		}

		public void Clear()
		{
			lock (m_lock)
			{
				m_items.Initialize();
				m_head = 0;
			}
		}
	}
}
