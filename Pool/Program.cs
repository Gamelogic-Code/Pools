// Raw pool implementation

using System.Collections.Generic;

Stack<Bat> inactiveObjects;

InitPool(100);

var bat = Get();
bat.Activate();

// Later...

bat.Deactivate();
Release(bat);

return;

void InitPool(int n)
{
	inactiveObjects = new Stack<Bat>();
	
	for (int i = 0; i < n; i++)
	{
		inactiveObjects.Push(new Bat());
	}
}

Bat Get() => inactiveObjects.Pop(); // what if the stack is empty
void Release(Bat obj) => inactiveObjects.Push(obj);

public class Bat
{
	public void Activate(){}
	public void Deactivate(){}
	
}
