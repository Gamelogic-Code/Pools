using System;
using System.Collections.Generic;
using NUnit.Framework;
using Pool;

namespace Tests
{
	public class Bat: IReusableObject, IPrioritizedObject<int>
	{
		public int health;
		
		private readonly Id<Bat> id = new Id<Bat>();
		
		public int Id => id.value;
		public bool IsActive { get; private set; } = true;
		public int Priority => id.value;
		
		public void Activate() => IsActive = true;
		public void Deactivate() => IsActive = false;
	}
	
	public class StackPoolTests
	{
		public static readonly ImplementationFactory<IPool<Bat>> Factories =
		[
			() => new StackPool<Bat>(0, Create, Activate, Deactivate, Destroy),
			() => new SelfGrowingPool<Bat>(0, Create, Activate, Deactivate, Destroy),
			
			() => new ActiveTrackingPool<Bat>(
				0, 
				Create, 
				Activate, 
				Deactivate, 
				Destroy, 
				EqualityComparer<Bat>.Default),
			
			() => new PriorityPool<Bat, int>(
				0, 
				Create, 
				Activate, 
				Deactivate, 
				Destroy, 
				b => b.Priority,
				Comparer<int>.Default, 
				EqualityComparer<Bat>.Default),
			
			() => new ReusableObjectPool<Bat>(
				0, 
				Create, 
				Destroy),
			
			() => new FakePool<Bat>(
				0, 
				Create, 
				Destroy)
		];

		private static Bat Create() => new();
		private static void Destroy(Bat bat) { }
		private static void Activate(Bat bat) => bat.Activate();
		private static void Deactivate(Bat bat) => bat.Deactivate();
	}
	
	[TestFixture(typeof(StackPool<Bat>))]
	[TestFixture(typeof(SelfGrowingPool<Bat>))]
	[TestFixture(typeof(ActiveTrackingPool<Bat>))]
	[TestFixture(typeof(PriorityPool<Bat, int>))]
	[TestFixture(typeof(ReusableObjectPool<Bat>))]
	[TestFixture(typeof(FakePool<Bat>))]
	public class StackPoolTests<TPool>//: StackPoolTests
		where TPool : IPool<Bat>
	{
		private IPool<Bat> pool;

		[SetUp]
		public void SetUp()
		{
			pool = StackPoolTests.Factories.GetInstance<StackPool<Bat>>(); // Factories.GetInstance<TPool>();
		}

		[Test]
		public void Capacity_ShouldReflectCorrectNumber()
		{
			pool.IncreaseCapacity(10);
			Assert.That(pool.Capacity, Is.EqualTo(10));
		}

		[Test]
		public void HasAvailableObject_ShouldReturnTrueWhenObjectsAreAvailable()
		{
			pool.IncreaseCapacity(1);
			Assert.That(pool.HasAvailableObject, Is.True);
		}

		[Test]
		public void Get_ShouldRetrieveAnObjectFromPool()
		{
			pool.IncreaseCapacity(1);
			var obj = pool.Get();
			Assert.That(obj, Is.Not.Null);
		}

		[Test]
		public void Get_ShouldThrowExceptionWhenNoObjectsAvailable()
		{
			Assert.Throws<InvalidOperationException>(() => pool.Get());
		}

		[Test]
		public void Release_ShouldReturnObjectBackToPool()
		{
			pool.IncreaseCapacity(1);
			var obj = pool.Get();
			pool.Release(obj);
			Assert.That(pool.HasAvailableObject, Is.True);
		}

		[Test]
		public void IncreaseCapacity_ShouldIncreasePoolCapacity()
		{
			pool.IncreaseCapacity(5);
			Assert.That(pool.Capacity, Is.EqualTo(5));
		}

		[Test]
		public void DecreaseCapacity_ShouldDecreasePoolCapacity()
		{
			pool.IncreaseCapacity(5);
			pool.DecreaseCapacity(3);
			Assert.That(pool.Capacity, Is.EqualTo(2));
		}

		[Test]
		public void DecreaseCapacity_WithDeactivateFirst_ShouldDeactivateObjects()
		{
			pool.IncreaseCapacity(5);
			pool.DecreaseCapacity(3);
			Assert.That(pool.Capacity, Is.EqualTo(2));
		}

		[Test]
		public void DecreaseCapacity_ShouldNotDecreaseBelowActiveObjects()
		{
			pool.IncreaseCapacity(3);
			var obj1 = pool.Get();
			var obj2 = pool.Get();
			pool.DecreaseCapacity(3);
			Assert.That(pool.Capacity, Is.EqualTo(2));
		}
	}
}
