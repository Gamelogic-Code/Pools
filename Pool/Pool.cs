using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Pool
{
public interface IPool<T> 
{
	int Capacity { get; }
	bool HasAvailableObject { get; }
	int InactiveObjectCount { get; }
	T Get();
	void Release(T obj);
	void IncreaseCapacity(int increment);
	int DecreaseCapacity(int decrement);
}

public interface ITrackedPool<T> : IPool<T>
{
	int ActiveObjectCount { get; }
	int ReleaseMin(int n);
}

public interface ISelfReleasingObject<out T>
{
	T Value { get; }
	void Release();
}

public interface IHashable<out T>
{
	T Value { get; }
}

public interface IReusableObject
{
	bool IsActive { get; }
	void Activate();
	void Deactivate();
	int Id { get; }
}

public interface IReusableObject<out T> : IReusableObject
{
	public T Value { get; }
}

public interface IPrioritizedObject<out TPriority>
{
	TPriority Priority { get; }
}

public class StackPool<T> : IPool<T>
{
	private readonly Stack<T> inactiveObjects = new();
	private readonly Func<T> createActive;
	private readonly Action<T> activate;
	private readonly Action<T> deactivate;
	private readonly Action<T> destroy;

	public int Capacity { get; private set; }
	public int InactiveObjectCount => inactiveObjects.Count;
	public bool HasAvailableObject => inactiveObjects.Count > 0;

	public StackPool(
		int capacity,
		Func<T> createActive,
		Action<T> activate,
		Action<T> deactivate,
		Action<T> destroy)
	{
		Capacity = capacity;
		this.createActive = createActive;
		this.activate = activate;
		this.deactivate = deactivate;
		this.destroy = destroy;

		CreateActive(capacity);
	}

	private void CreateActive(int capacity)
	{
		for (int i = 0; i < capacity; i++)
		{
			var newObject = createActive();
			deactivate(newObject);
			inactiveObjects.Push(newObject);
		}
	}

	public T Get()
	{
		if (inactiveObjects.Count == 0)
		{
			throw new InvalidOperationException("No available objects");
		}

		var obj = inactiveObjects.Pop();
		activate(obj);
		return obj;
	}

	public void Release(T obj)
	{
		deactivate(obj);
		inactiveObjects.Push(obj);
	}

	public void IncreaseCapacity(int increment)
	{
		Capacity += increment;
		CreateActive(increment);
	}

	/// <summary>
	/// Decrease up to the number of elements currently inactive.
	/// </summary>
	/// <param name="decrement"></param>
	public int DecreaseCapacity(int decrement)
	{
		int inactiveDecrement = inactiveObjects.Count < decrement ? inactiveObjects.Count : decrement;
		
		for (int i = 0; i < inactiveDecrement; i++)
		{
			var obj = inactiveObjects.Pop();
			destroy(obj);
		}

		Capacity -= inactiveDecrement;
		return inactiveDecrement;
	}
}

public class FakePool<T> : IPool<T>
{
	private readonly Func<T> create;
	private readonly Action<T> destroy;
	
	public int Capacity { get; private set; }
	public bool HasAvailableObject => InactiveObjectCount > 0;

	public int InactiveObjectCount { get; private set; }
	
	public FakePool(
		int capacity,
		Func<T> create,
		Action<T> destroy
	)
	{
		Capacity = capacity;
		this.create = create;
		this.destroy = destroy;
	}

	public T Get()
	{
		if (!HasAvailableObject)
		{
			throw new InvalidOperationException("No available objects");
		}
		
		InactiveObjectCount--;
		return create();
	}

	public void Release(T obj)
	{
		destroy(obj);
		InactiveObjectCount++;
	}

	public void IncreaseCapacity(int increment) => Capacity += increment;

	public int DecreaseCapacity(int decrement) => Capacity -= decrement;
}

public class ActiveTrackingPool<T> : ITrackedPool<T>
{
	private readonly HashSet<T> activeObjects;
	private readonly StackPool<T> pool;
	
	public int Capacity => pool.Capacity;
	public int ActiveObjectCount => activeObjects.Count;
	public int InactiveObjectCount => pool.InactiveObjectCount;
	public bool HasAvailableObject => pool.HasAvailableObject;
	
	public ActiveTrackingPool(
		int initialCapacity, 
		Func<T> createActive, 
		Action<T> activate,
		Action<T> deactivate, 
		Action<T> destroy, 
		IEqualityComparer<T> comparer)
	{
		activeObjects = new HashSet<T>(comparer);
		pool = new StackPool<T>(initialCapacity, createActive, activate, deactivate, destroy);
	}
	
	public T Get()
	{
		var obj = pool.Get();
		Debug.Assert(!activeObjects.Contains(obj));
		activeObjects.Add(obj);
		
		return obj;
	}

	public void Release(T obj)
	{
		// Here an assert is not enough since the user could call Release more than once on an object
		if (!activeObjects.Contains(obj))
		{
			throw new InvalidOperationException("Object not active");
		}
		
		pool.Release(obj);
		activeObjects.Remove(obj);
	}

	public void IncreaseCapacity(int increment)
	{
		pool.IncreaseCapacity(increment);
	}

	public int DecreaseCapacity(int decrement)
	{
		int inactiveDecrement = pool.DecreaseCapacity(decrement);
	
		return inactiveDecrement;
	}
	
	public int ReleaseMin(int n)
	{
		int count = 0;
		foreach (var obj in activeObjects.TakeWhile(_ => count != n))
		{
			pool.Release(obj);
			count++;
		}

		return count;
	}
}

public class SelfGrowingPool<T> : IPool<T>
{
	public StackPool<T> pool;
	
	public int Capacity => pool.Capacity;
	
	public int InactiveObjectCount => pool.InactiveObjectCount;

	public bool HasAvailableObject => pool.HasAvailableObject;

	public SelfGrowingPool(
		int initialCapacity,
		Func<T> createActive,
		Action<T> activate,
		Action<T> deactivate,
		Action<T> destroy) 
		=> pool = new StackPool<T>(initialCapacity, createActive, activate, deactivate, destroy);

	public T Get()
	{
		if (!pool.HasAvailableObject)
		{
			pool.IncreaseCapacity(1);
		}

		return pool.Get();
	}

	public void Release(T obj) => pool.Release(obj);

	public void IncreaseCapacity(int increment) => pool.IncreaseCapacity(increment);

	public int DecreaseCapacity(int decrement) => pool.DecreaseCapacity(decrement);
}

public class PriorityPool<T, TPriority> : ITrackedPool<T>
{
	private readonly Action<T> reactivate;
	private readonly Func<T, TPriority> getPriority;
	public StackPool<T> pool;
	private readonly IndexPriorityQueue<T, TPriority> activeObjects;
	private readonly Dictionary<T, int> objectToIndex;

	public int Capacity => pool.Capacity;
	
	public int ActiveObjectCount => activeObjects.Count;

	public int InactiveObjectCount => pool.InactiveObjectCount;

	public bool HasAvailableObject => pool.HasAvailableObject;
	
	public PriorityPool(
		int initialCapacity,
		Func<T> createActive,
		Action<T> activate,
		Action<T> reactivate,
		Action<T> deactivate,
		Func<T, TPriority> getPriority,
		IComparer<TPriority> priorityComparer, 
		IEqualityComparer<T> objectComparer)
	{
		int objectCount = 0;

		this.reactivate = reactivate;
		this.getPriority = getPriority;
		pool = new StackPool<T>(initialCapacity, CreateActive, activate, deactivate, Destroy);
		activeObjects = new IndexPriorityQueue<T, TPriority>(initialCapacity, priorityComparer);
		objectToIndex = new Dictionary<T, int>(objectComparer);
	
		return;

		T CreateActive()
		{
			var obj = createActive();
			objectToIndex[obj] = objectCount++;
			return obj;
		}
		
		// Nothing to do
		void Destroy(T _){}
	}

	public T Get()
	{
		T obj;
		int index;
	
		if (pool.HasAvailableObject)
		{
			obj = pool.Get();
			index = objectToIndex[obj];
		}
		else
		{
			(index, obj) = activeObjects.Dequeue();
			reactivate(obj);
		}
	
		activeObjects.Enqueue(index, obj, getPriority(obj));
		return obj;
	}

	public void ReleaseMin()
	{
		(int _, var obj) = activeObjects.Dequeue();
		Release(obj);
	}

	public void Release(T obj)
	{
		pool.Release(obj);
		activeObjects.Remove(objectToIndex[obj]);
	}
	
	public int ReleaseMin(int count)
	{
		int releasedCount = 0;
		while (releasedCount < count && !activeObjects.IsEmpty)
		{
			ReleaseMin();
			releasedCount++;
		}

		return releasedCount;
	}
	
	public void UpdatePriority(T obj)
	{
		int index = objectToIndex[obj];
		if (!activeObjects.Contains(index))
		{
			throw new InvalidOperationException("Object is not active");
		}

		activeObjects.UpdateValue(index, obj, getPriority(obj));
	} 

	public void IncreaseCapacity(int increment) => throw new NotSupportedException();

	public int DecreaseCapacity(int decrement) => throw new NotSupportedException();
}

public class SelfReleasingObjectPool<T> : IPool<ISelfReleasingObject<T>>
{
	private class SelfReleasingObject : ISelfReleasingObject<T>
	{
		private readonly IPool<SelfReleasingObject> owner;
		public T Value { get; }
		
		public SelfReleasingObject(T value, IPool<SelfReleasingObject> owner)
		{
			Value = value;
			this.owner = owner;
		}
		
		public void Release() => owner.Release(this);
	}

	private readonly StackPool<SelfReleasingObject> pool;
	
	public int Capacity => pool.Capacity;
	
	public int InactiveObjectCount => pool.InactiveObjectCount;
	
	public bool HasAvailableObject => pool.HasAvailableObject;
	
	public SelfReleasingObjectPool(
		int initialCapacity,
		Func<T> createActive,
		Action<T> activate,
		Action<T> deactivate,
		Action<T> destroy)
	{
		pool	= new StackPool<SelfReleasingObject>(initialCapacity, CreateActive, Activate, Deactivate, Destroy);
		return;

		SelfReleasingObject CreateActive()
		{
			var obj = new SelfReleasingObject(createActive(), pool);
			return obj;
		}
		
		void Destroy(SelfReleasingObject obj)
		{
			destroy(obj.Value);
		}
		
		void Activate(SelfReleasingObject obj) => activate(obj.Value);
		void Deactivate(SelfReleasingObject obj) => deactivate(obj.Value);
	}
	
	public ISelfReleasingObject<T> Get()
	{
		var obj = pool.Get();
		return obj;
	}
	
	public void Release(ISelfReleasingObject<T> obj) => pool.Release((SelfReleasingObject)obj);

	public void IncreaseCapacity(int increment) => pool.IncreaseCapacity(increment);

	public int DecreaseCapacity(int decrement)
		=> pool.DecreaseCapacity(decrement);
}

public class ReusableObjectPool : IPool<IReusableObject>
{
	private readonly StackPool<IReusableObject> pool;

	public int Capacity => pool.Capacity;
	
	public int InactiveObjectCount => pool.InactiveObjectCount;

	public bool HasAvailableObject => pool.HasAvailableObject;
	
	public ReusableObjectPool(int initialCapacity, 
		Func<IReusableObject> createActive,
		Action<IReusableObject> destroy)
	{
		pool = new StackPool<IReusableObject>(initialCapacity, createActive, Activate, Deactivate, destroy);
	
		return;
	
		void Activate(IReusableObject obj) => obj.Activate();
		void Deactivate(IReusableObject obj) => obj.Deactivate();
	}

	public IReusableObject Get() => pool.Get();

	public void Release(IReusableObject obj) => pool.Release(obj);

	public void IncreaseCapacity(int increment) => pool.IncreaseCapacity(increment);

	public int DecreaseCapacity(int decrement) 
		=> pool.DecreaseCapacity(decrement);
}

public class ReusableObjectPool<T> : IPool<T>
	where T : IReusableObject
{
	private readonly StackPool<T> pool;

	public int Capacity => pool.Capacity;
	
	public bool HasAvailableObject => pool.HasAvailableObject;
	public int InactiveObjectCount => pool.InactiveObjectCount;

	public ReusableObjectPool(int initialCapacity, 
		Func<T> createActive,
		Action<T> destroy)
	{
		pool = new StackPool<T>(initialCapacity, createActive, Activate, Deactivate, destroy);
	
		return;
	
		void Activate(T obj) => obj.Activate();
		void Deactivate(T obj) => obj.Deactivate();
	}

	public T Get() => pool.Get();
	public void Release(T obj) => pool.Release(obj);
	public void IncreaseCapacity(int increment) => pool.IncreaseCapacity(increment);
	public int DecreaseCapacity(int decrement) => pool.DecreaseCapacity(decrement);
}

public class PriorityReusableObjectPool<T, TPriority> 
	where T : IReusableObject, IPrioritizedObject<TPriority>
{
	private readonly StackPool<T> pool;
	private readonly IndexPriorityQueue<T, TPriority> activeObjects;

	public PriorityReusableObjectPool(
		int capacity,
		Func<T> createActive,
		Action<T> destroy, 
		IEqualityComparer<T> objectComparer,
		IComparer<TPriority> priorityComparer)
	{
		int counter = 0;

		var objectToIndex = new Dictionary<T, int>(objectComparer);
		activeObjects = new IndexPriorityQueue<T, TPriority>(capacity, priorityComparer);
		pool = new StackPool<T>(capacity, CreateActive, Activate, Deactivate, destroy);
	
		return;

		T CreateActive()
		{
			var obj = createActive();
			objectToIndex[obj] = counter++;
			return obj;
		}

		void Activate(T obj)
		{
			obj.Activate();
			activeObjects.Enqueue(objectToIndex[obj], obj, obj.Priority);
		}

		void Deactivate(T obj)
		{
			obj.Deactivate();
			activeObjects.Remove(objectToIndex[obj]);
		}
	}

	public T Get()
	{
		if (pool.HasAvailableObject)
		{
			return pool.Get();
		}

		(int index, var obj) = activeObjects.Dequeue();
		obj.Deactivate();
		obj.Activate();
		activeObjects.Enqueue(index, obj, obj.Priority);
		return obj;
	}

	public void Release(T obj) => pool.Release(obj);
}

public interface IEnemy
{
	public int Health { get; set; }
}

public class EnemyPool
{
	private readonly ReusableObjectPool<Enemy> pool;

	private class Enemy : IEnemy, IReusableObject
	{
		public int Health { get; set; }
		public bool IsActive { get; private set;}

		private readonly Id<Enemy> id = new();
		public int Id => id.value;
	
		public void Activate()
		{
			Debug.Assert(!IsActive);
			// Make active in scene
			IsActive = true;
		}

		public void Deactivate()
		{
			Debug.Assert(IsActive);
			// Make inactive in scene
			IsActive = false;
		}

		public override int GetHashCode() => id.value;
	}

	public EnemyPool(int initialCapacity) => pool = new ReusableObjectPool<Enemy>(initialCapacity, CreateEnemy, DestroyEnemy);

	public IEnemy Get() => pool.Get();
	public void Release(IEnemy enemy) => pool.Release((Enemy)enemy);

	private static Enemy CreateEnemy() => new();

	private static void DestroyEnemy(Enemy enemy) { }
}

public static class Pools
{
	private class IdHashable<T> : IHashable<T>
	{
		private readonly Id<IHashable<T>> id = new();
		public T Value { get; }

		public override int GetHashCode() => id.GetHashCode();
		
		public IdHashable(T value) => Value = value;
	}

	private class ReusableObject<T> : IReusableObject<T>
	{
		private readonly Id<ReusableObject<T>> id = new();
		
		public bool IsActive { get; set; }
	
		public T Value { get; }

		public int Id => id.value;

		private readonly Action<T> activate;
		private readonly Action<T> deactivate;
	
		public void Activate()
		{
			Debug.Assert(!IsActive, "Object is already active");
			activate(Value);
		}

		public void Deactivate()
		{
			Debug.Assert(IsActive, "Object is already inactive");
			deactivate(Value);
		}

		public ReusableObject(T value, Action<T> activate, Action<T> deactivate)
		{
			Value = value;
			IsActive = true;
			this.activate = activate;
			this.deactivate = deactivate;
		}
	}
	
	public interface IPrioritizedReusableObject<out T, out TPriority> : IReusableObject, IPrioritizedObject<TPriority>
	{
		public T Value { get; }
	}

	// This class will not work when priorities change. 
	private class PrioritizedReusableObject<T, TPriority> : IPrioritizedReusableObject<T, TPriority>
	{
		private class Comparer : IEqualityComparer<IPrioritizedReusableObject<T, TPriority>>
		{
			private readonly IEqualityComparer<T> baseComparer;

			public Comparer(IEqualityComparer<T> baseComparer)
			{
				this.baseComparer = baseComparer;
			}

			public bool Equals(IPrioritizedReusableObject<T, TPriority> x, IPrioritizedReusableObject<T, TPriority> y)
			{
				if (ReferenceEquals(x, y)) return true;
				if (x is null) return false;
				if (y is null) return false;
				if (x.GetType() != y.GetType()) return false;
				return baseComparer.Equals(x.Value, y.Value);
			}
			
			public int GetHashCode(IPrioritizedReusableObject<T, TPriority> obj)
			{
				return EqualityComparer<T>.Default.GetHashCode(obj.Value);
			}
		}
		
		private readonly Id<PrioritizedReusableObject<T, TPriority>> id = new();
		
		public bool IsActive { get; set; }

		public T Value { get; }

		public TPriority Priority => getPriority(Value);

		public int Id => id.value;

		private readonly Action<T> activate;
		private readonly Action<T> deactivate;
		private readonly Func<T,TPriority> getPriority;

		public void Activate()
		{
			Debug.Assert(!IsActive, "Object is already active");
			activate(Value);
		}

		public void Deactivate()
		{
			Debug.Assert(IsActive, "Object is already inactive");
			deactivate(Value);
		}

		public PrioritizedReusableObject(T value, Action<T> activate, Action<T> deactivate, Func<T, TPriority> getPriority)
		{
			Value = value;
			IsActive = true;
			this.activate = activate;
			this.deactivate = deactivate;
			this.getPriority = getPriority;
		}

		public override int GetHashCode() => Id;

		public static IEqualityComparer<IPrioritizedReusableObject<T, TPriority>> GetComparer(IEqualityComparer<T> baseComparer) 
			=> new Comparer(baseComparer);
	}
	
	public static IPool<T> GetPoolWithActiveTracking<T>(
		int initialCapacity, 
		Func<T> createActive, Action<T> activate,
		Action<T> deactivate, 
		Action<T> destroy,
		IEqualityComparer<T> comparer)
	{
		var activeObjects = new HashSet<T>(comparer);
		return new StackPool<T>(initialCapacity, createActive, Activate, Deactivate, destroy);
		
		void Activate(T obj)
		{
			if (!activeObjects.Add(obj))
			{
				throw new InvalidOperationException("Object is already active");
			}
			
			activate(obj);
		}
		
		void Deactivate(T obj)
		{
			if (!activeObjects.Remove(obj))
			{
				throw new InvalidOperationException("Object not active");
			}

			deactivate(obj);
		}
	}
	
	public static IPool<IHashable<T>> GetPoolWithActiveTrackingForNonHashables<T>(
		int initialCapacity, 
		Func<T> createActive, 
		Action<T> activate,
		Action<T> deactivate, 
		Action<T> destroy)
		where T : class
	{
		return GetPoolWithActiveTracking(initialCapacity, CreateActive, Activate, Deactivate, Destroy, null);
		
		IHashable<T> CreateActive() => new IdHashable<T>(createActive());
		void Destroy(IHashable<T> obj) => destroy(obj.Value);
		void Activate(IHashable<T> obj) => activate(obj.Value);
		void Deactivate(IHashable<T> obj) => deactivate(obj.Value);
	}
	
	public static ITrackedPool<IHashable<T>> GetActiveTrackingPoolForNonHashables<T>(
		int initialCapacity, 
		Func<T> createActive, 
		Action<T> activate,
		Action<T> deactivate, 
		Action<T> destroy) 
		where T : class // Our method of wrapping does not work for structs that have value equality
							 // For structs, you are better off using a custom comparer. 
	{
		return new ActiveTrackingPool<IHashable<T>>(initialCapacity, CreateActive, Activate, Deactivate, Destroy, null);
		
		IHashable<T> CreateActive() => new IdHashable<T>(createActive());
		void Destroy(IHashable<T> obj) => destroy(obj.Value);
		void Activate(IHashable<T> obj) => activate(obj.Value);
		void Deactivate(IHashable<T> obj) => deactivate(obj.Value);
	}
	
	public static PriorityPool<T, int> GetPriorityPool<T>(
		int initialCapacity,
		Func<T> createActive,
		Action<T> activate,
		Action<T> reactivate, // can be an optimized version of deactivate followed by activate
		Action<T> deactivate, 
		IEqualityComparer<T> comparer)
	{
		IDictionary<T, int> objectCreationTime = new Dictionary<T, int>(comparer);
		int creationTime = 0;
	
		var pool = new PriorityPool<T, int>(
			initialCapacity,
			createActive,
			Activate,
			Reactivate,
			deactivate,
			obj => objectCreationTime[obj],
			Comparer<int>.Default, 
			comparer
			);

		return pool;

		void Activate(T obj)
		{
			activate(obj);
			objectCreationTime[obj] = creationTime++;
		}
	
		void Reactivate(T obj)
		{
			reactivate(obj);
			objectCreationTime[obj] = creationTime++;
		}
	}
	
	// What is nasty about this pool is that the user has to cast the object IPooledUserObject<T> to get the value. 
	public static ReusableObjectPool GetNonGenericReusableObjectPool<T>(int initialCapacity, 
		Func<T> createActive,
		Action<T> activate,
		Action<T> deactivate,
		Action<T> destroy)
		where T : IReusableObject
	{
		return new ReusableObjectPool(initialCapacity, CreateActive, Destroy);
	
		ReusableObject<T> CreateActive() => new(createActive(), activate, deactivate);
		void Destroy(IReusableObject obj) => destroy(((ReusableObject<T>)obj).Value);
	}
	
	
	public static PriorityReusableObjectPool<IPrioritizedReusableObject<T, TPriority>, TPriority> 
		GetPriorityReusableObjectPool<T, TPriority>(
			int initialCapacity,
			Func<T> createActive,
			Action<T> activate,
			Action<T> deactivate,
			Action<T> destroy,
			Func<T, TPriority> getPriority,
			IEqualityComparer<T> comparer,
			IComparer<TPriority> priorityComparer)
	{
		return new PriorityReusableObjectPool<IPrioritizedReusableObject<T, TPriority>, TPriority>(
			initialCapacity, 
			CreateActive, 
			Destroy,
			PrioritizedReusableObject<T, TPriority>.GetComparer(comparer),
			priorityComparer
			);
		PrioritizedReusableObject<T, TPriority> CreateActive() => new(createActive(), activate, deactivate, getPriority);
		void Destroy(IPrioritizedReusableObject<T, TPriority> obj) => destroy(obj.Value);
	}
	
	public static ReusableObjectPool<IReusableObject<T>> GetReusableObjectPool<T>(int initialCapacity, 
		Func<T> createActive,
		Action<T> activate,
		Action<T> deactivate,
		Action<T> destroy)
	{
		return new ReusableObjectPool<IReusableObject<T>>(initialCapacity, CreateActive, Destroy);
	
		IReusableObject<T> CreateActive() => new ReusableObject<T>(createActive(), activate, deactivate);
		void Destroy(IReusableObject<T> obj) => destroy(obj.Value);
	}
	
	public static ReusableObjectPool<IReusableObject<T>> GetReusableObjectPoolWithTracking<T>(int initialCapacity, 
		Func<T> createActive,
		Action<T> activate,
		Action<T> deactivate,
		Action<T> destroy,
		IEqualityComparer<T> comparer)
	{
		var activeObjects = new HashSet<T>(comparer);
	
		return new ReusableObjectPool<IReusableObject<T>>(initialCapacity, CreateActive, Destroy);
	
		ReusableObject<T> CreateActive() => new(createActive(), Activate, Deactivate);
	
		void Activate(T obj)
		{
			if (!activeObjects.Add(obj))
			{
				throw new InvalidOperationException("Object is already active");
			}
			
			activate(obj);
		}
	
		void Deactivate(T obj)
		{
			if (!activeObjects.Remove(obj))
			{
				throw new InvalidOperationException("Object is already inactive");
			}

			deactivate(obj);
		}
	
		void Destroy(IReusableObject<T> obj)
		{
			activeObjects.Remove(obj.Value); // This will have no effect if obj is not in the set
			destroy(obj.Value);
		}
	}
}

public static class PoolExtensions
{
	public static bool TryGet<T>(this ITrackedPool<T> pool, out T obj)
	{
		if (pool.HasAvailableObject)
		{
			obj = pool.Get();
			return true;
		}

		obj = default;
		return false;
	}
}
}
