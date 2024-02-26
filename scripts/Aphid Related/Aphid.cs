using System;
using System.Collections.Generic;
using Godot;

public partial class Aphid : CharacterBody2D
{
	public AphidInstance Instance;

	[Export] private Node2D spriteBody;
	[Export] private Sprite2D eyes, antenna, body, back_legs, front_legs;
	[Export] private Area2D triggerArea;

	[ExportCategory("Effects")]
	[Export] private PackedScene heartParticles;
	[Export] private PackedScene foodParticles, sleepParticles, angerParticles;

	// Aphid state
	public enum AphidState { Idle, Eating, Drinking, Sleeping, Playing, Petting }
	public AphidState OurState = AphidState.Idle;

	// Idling Params
	private Vector2 idlePosition;
	private int idleRange = 100;
	private float idle_timer, idle_timeout_timer;
	private const float idle_timer_range = 1.15f, idle_timeout = 4f;
	private Timer blink_timer = new(), blink_duration_timer = new();

	// Eating Params
	private Node2D food_item;
	private Vector2 food_item_position, food_item_direction;
	private int foodgobble_shutter_speed;
	private bool IsFoodFavorite, FoodMode, food_item_switch;
	private float gobble_timer, hunger_decay_timer;
	private const float gobble_duration = 2f, hunger_decay = 10.5f, food_pursue_duration = 5f;
	private Timer food_pursue_timer = new(), food_gc_timer = new();
	private readonly List<Node2D> food_ignore_list = new();
	// Drinking Params (Drinking shares values with Eating but not the other way around)
	private float thirst_decay_timer;
	private const float thirst_decay = 6.8f; 

	// Sleeping Params
	private float sleep_decay_timer, sleep_gain_timer, try_sleep_timer, try_wake_timer;
	private const float sleep_decay = 10.5f, sleep_gain = 4.5f, try_sleep = 2.4f, try_wake = 2.4f;
	private GpuParticles2D sleep_effect;
	private float[] sleep_weights = new float[] { 90, 10 };

	// Petting Params And Affection too
	private float pet_timer, affection_decay_timer;
	private const float pet_duration = 2f, affection_decay = 13.2f;

	// Movement Params
	public Vector2 MovementDirection;
	public float MovementSpeed;
	private int walk_shutter_speed;
	private bool legsStep, flipSwitch;
	private Vector2 front_legs_position, back_legs_position;

	// General Params
	private RandomNumberGenerator behaviourRNG = new();
	public readonly Dictionary<string, Action<Node2D>> TriggerActions = new();
	private float[] player_seek_weights = new float[] { 55, 15, 30 };
	private float interaction_cd_timer;
	private const float interaction_cd = 1.35f;

    public override void _Ready()
    {
		// ===| Set Default Params |===
		idlePosition = GlobalPosition;
		front_legs_position = front_legs.Position;
		back_legs_position = back_legs.Position;
		food_item_position = new(25,0)
;
		triggerArea.BodyEntered += OnTriggerEnter;
		TriggerActions.Add("drink", (Node2D n) => OnFoodTrigger(n, false));
		TriggerActions.Add("food", (Node2D n) => OnFoodTrigger(n));
		TriggerActions.Add("player", OnPlayerTrigger);

		// Timers
		AddChild(blink_duration_timer);
		AddChild(blink_timer);
		AddChild(food_pursue_timer);
		AddChild(food_gc_timer);

		blink_duration_timer.OneShot = true;
		blink_timer.OneShot = true;
		food_pursue_timer.OneShot = true;

		blink_timer.Timeout += () =>
		{
			if (OurState == AphidState.Sleeping)
				return;
			SetSkin("blink", true, false, false, false);
			blink_duration_timer.Start(0.1f);
		};
        blink_duration_timer.Timeout += () => 
		{
			if (OurState == AphidState.Sleeping)
				return;
			SetSkin("idle", true, false, false, false);
			blink_timer.Start(GameManager.RNG.RandfRange(4.5f, 6.7f));
		};
		blink_timer.Start(GameManager.RNG.RandfRange(4.5f, 6.7f));
		food_pursue_timer.Timeout += () => 
		{
			food_ignore_list.Add(food_item);
			food_item = null;
			SetAphidState(AphidState.Idle);
		};
		food_gc_timer.Timeout += () => food_ignore_list.Clear();
		food_gc_timer.Start(30);

		// Set Aphid Data
		SetSkin("idle", true);
		MovementSpeed = 20 + (0.15f * Instance.Status.speed.Level);
    }

	// ==========| Update Processes |===========
    public override void _Process(double delta)
    {
		float _delta = (float)delta;
		ZIndex = (int)GlobalPosition.Y;

		Instance.Status.PositionX = GlobalPosition.X;
		Instance.Status.PositionY = GlobalPosition.Y;
		Instance.Status.Age += _delta;

		TickInteractionCooldown(_delta);
		TickAffectionDecay(_delta);
		TickHungerDecay(_delta);
		TickThirstDecay(_delta);
		TickSleepDecay(_delta);
    }
    public override void _PhysicsProcess(double delta)
    {
		float _delta = (float)delta;
        Velocity = MovementDirection * MovementSpeed;
		var _collisionList = triggerArea.GetOverlappingBodies();

	 	if (_collisionList.Count > 0)
		{
			for (int i = 0; i < _collisionList.Count; i++)
				OnTriggerEnter(_collisionList[i]);
		}

		switch(OurState)
		{
			case AphidState.Idle:
				Idle(_delta);
			break;
			case AphidState.Eating:
			case AphidState.Drinking:
				if (gobble_timer > 0)
					TickFoodGobble(_delta);
				else
					WaddleToFood();
			break;
			case AphidState.Petting:
				TickPetTime(_delta);
			break;
			case AphidState.Sleeping:
				TickSleepRecover(_delta);
			break;
		}

		MoveAndSlide();
		DoWalkAnim();
		if (MovementDirection != Vector2.Zero)
			SetFlipDirection(MovementDirection);
		TickFlip(_delta);
    }

	public void SetAphidState(AphidState _new)
	{
		// Properly disposed of previous state if needed
		SetMovementDirection(Vector2.Zero);
		switch(OurState)
		{
			case AphidState.Eating:
			case AphidState.Drinking:
				food_item = null;
				IsFoodFavorite = false;
			break;
			case AphidState.Sleeping:
				SetSkin("idle", true);
				spriteBody.Position = new(0,0);
				sleep_effect.Emitting = false;
				sleep_effect = null;
			break;
		}

		// Setup new state as needed
		switch(_new)
		{
			case AphidState.Idle:
				idle_timer = behaviourRNG.RandfRange(idle_timer_range, idle_timer_range * 2);
				idlePosition = GlobalPosition;
			break;
			case AphidState.Petting:
				pet_timer = pet_duration;
			break;
		}

		OurState = _new;
	}

	// State functions
    protected void Idle(float _delta)
	{
		// standing still wait time
		if (idle_timer > 0)
		{
			idle_timer -= _delta;
			return;
		}
		
		// we are close to idle pos, generate a new one and stand still for a few seconds
		if (GlobalPosition.DistanceTo(idlePosition) < 20)
		{
			idlePosition = new Vector2(behaviourRNG.RandfRange(-idleRange, idleRange), behaviourRNG.RandfRange(-idleRange, idleRange)) + GlobalPosition;
			SetMovementDirection(Vector2.Zero);
			idle_timer = behaviourRNG.RandfRange(idle_timer_range, idle_timer_range * 2);
			return;
		}

		// move to idle pos, timeout if you cant
		SetMovementDirection(idlePosition - GlobalPosition);
		idle_timeout_timer += _delta;
		if (idle_timeout_timer > idle_timeout)
		{
			idlePosition = GlobalPosition;
			idle_timeout_timer = 0;
		}
	}
	protected void WaddleToFood()
	{	
		// if is not valid, too far away, or claimed by someone, let go
		if (!IsInstanceValid(food_item) || GlobalPosition.DistanceTo(food_item.GlobalPosition) > 200 || !food_item.HasMeta("tag"))
		{
			SetAphidState(AphidState.Idle);
			return;
		}

		// If close, eat it, else try walk to it
		if (GlobalPosition.DistanceTo(food_item.GlobalPosition) < 30)
		{
			SetMovementDirection(Vector2.Zero);
			food_item.RemoveMeta("tag"); // Stops others from eating it
			food_item.SetMeta("pickup", false); // Stop picking it up
			food_item_direction = flipSwitch ? -food_item_position : food_item_position;
			food_item.GlobalPosition = GlobalPosition + food_item_direction;
			(food_item.GetChild(1) as CollisionShape2D).Disabled = true;
			food_pursue_timer.Stop();
			gobble_timer = gobble_duration;
		}
		else if (food_pursue_timer.TimeLeft > 0)	
			SetMovementDirection(food_item.GlobalPosition - GlobalPosition);
	}
	protected void TickFoodGobble(float _delta)
	{
		// Gobbling it up, yum yum
		gobble_timer -= _delta;

		// eating motion
		if (foodgobble_shutter_speed == 0)
		{
			food_item.GlobalPosition = GlobalPosition + food_item_direction 
			+ (food_item_switch ? Vector2.Up : Vector2.Zero);
			food_item_switch = !food_item_switch;
			foodgobble_shutter_speed = 8;
		}
		else
			foodgobble_shutter_speed--;

		// finished meal
		if (gobble_timer <= 0)
		{
			// Dispose of the food item now
			if (IsInstanceValid(food_item)) 
			{
				if (FoodMode)
					SetHunger((int)(10 * Instance.Genes.FoodMultipliers[(int)food_item.GetMeta("food_type")]));
				else
					SetThirst((int)(10 * Instance.Genes.FoodMultipliers[(int)food_item.GetMeta("food_type")]));
				
				food_item.QueueFree();
			}
			SetAphidState(AphidState.Idle);
		}
	}
	public bool Pet()
	{
		// no petties while crunching grub, or already petting
		if (gobble_timer > 0 || OurState == AphidState.Petting)
			return false;

		// Get ANGY if awoken
		if (OurState == AphidState.Sleeping)
		{
			WakeUp(true);
			return false;
		}

		SetAphidState(AphidState.Petting);
		return true;
	}
	protected void TickPetTime(float _delta)
	{
		if (pet_timer > 0)
			pet_timer -= _delta;
		else
		{
			// If low on affection, raise bondship too
			if (Instance.Status.Affection < 80)
			{
				GameManager.EmitParticles(heartParticles, GlobalPosition - new Vector2(0, 10));
				SetBondship(1);
			}
			SetAffection(10);
			SetAphidState(AphidState.Idle);
		}
	}
	protected void Sleep()
	{
		if (OurState != AphidState.Idle)
			return;

		// SLEEP!!!!!
		SetSkin("sleep", true, false, false, true);
		sleep_effect = GameManager.EmitParticles(sleepParticles, GlobalPosition);
		spriteBody.Position = new(0,2);
		SetAphidState(AphidState.Sleeping);
	}
	public void WakeUp(bool _forcefully = false)
	{
		SetAphidState(AphidState.Idle);
		if (!_forcefully)
			return;
		SetAffection(-5);
		var _particles = GameManager.EmitParticles(angerParticles, new());
		_particles.ProcessMode = ProcessModeEnum.Always;
		_particles.GetParent().RemoveChild(_particles);
		AddChild(_particles);
	}
	protected void TickSleepRecover(float _delta)
	{
		if (sleep_gain_timer > 0)
			sleep_gain_timer -= _delta;
		else
		{
			SetSleepiness(behaviourRNG.RandfRange(0.75f, 1.95f));
			sleep_gain_timer = sleep_gain;
		}

		if (Instance.Status.Sleepiness < 80)
			return;

		// enough sleep, try waking up
		if (try_wake_timer > 0)
			try_wake_timer -= _delta;
		else if (GameManager.GetRandomByWeight(behaviourRNG, sleep_weights) != 1)
			try_wake_timer = try_wake;
		else
			WakeUp();
	}

	// General Functions
	public void SetMovementDirection(Vector2 _direction)
	{
		MovementDirection = _direction.Normalized();
	}
	protected virtual void DoWalkAnim()
	{
		if(MovementDirection == Vector2.Zero)
		{
			// Reset back to idle standing
			front_legs.Position = front_legs_position;
			back_legs.Position = back_legs_position;
			walk_shutter_speed = 0;
			return;
		}

		// Motion Framerate
		if (walk_shutter_speed > 0)
		{
			walk_shutter_speed--;
			return;
		}
		walk_shutter_speed = 4;
		legsStep = !legsStep;

		// Switch between back and front legs to make the walking motion
		front_legs.Position = front_legs_position + (legsStep ? new Vector2(0,-1) : Vector2.Zero);
		back_legs.Position = back_legs_position + (legsStep ? Vector2.Zero : new Vector2(0,-1));
	}
	public void SetFlipDirection(Vector2 _direction, bool _setAsCurrent = false)
	{
		// False : Facing Right - True : Facing Left
		if (_direction.X < 0)
			flipSwitch = true;
		else if (_direction.X > 0)
			flipSwitch = false;

		if (_setAsCurrent)
			spriteBody.Scale = new(flipSwitch ? 1 : -1, spriteBody.Scale.Y);
	}
	private void TickFlip(float _delta)
	{
		// False : Facing Right - True : Facing Left
		if (flipSwitch)
			spriteBody.Scale = new(Mathf.Lerp(spriteBody.Scale.X, 1, _delta * 3), spriteBody.Scale.Y);
		else
			spriteBody.Scale = new(Mathf.Lerp(spriteBody.Scale.X, -1, _delta * 3), spriteBody.Scale.Y);
	}
	public void SetSkin(string _action, bool _eyes = false, bool _antenna = true, bool _body = true, bool _legs = true)
	{
		if (_eyes)
		{
			eyes.Texture = GetSkinPiece("eyes", _action, Instance.Genes.EyeType);
			eyes.SelfModulate = Instance.Genes.EyeColor;
		}
		if (_antenna)
		{
			antenna.Texture = GetSkinPiece("antenna", _action, Instance.Genes.EyeType);
			antenna.SelfModulate = Instance.Genes.AntennaColor;
		}
		if (_body)
		{
			body.Texture = GetSkinPiece("body", _action, Instance.Genes.EyeType);;
			body.SelfModulate = Instance.Genes.BodyColor;
		}
		if (_legs)
		{
			Texture2D _legsTexture = GetSkinPiece("legs", _action, Instance.Genes.LegType);
			front_legs.SelfModulate = back_legs.SelfModulate = Instance.Genes.LegColor;
			front_legs.Texture = back_legs.Texture = _legsTexture;
		}
	}
	private static Texture2D GetSkinPiece(string _piece, string _action, int _id)
	{
		string _path = $"{GameManager.SkinsPath}/{_piece}_{_action}_{_id}";
		if (FileAccess.FileExists(_path + ".png"))
			_path += ".png";
		else if (FileAccess.FileExists(_path + ".PNG"))
			_path += ".PNG";
		else if (FileAccess.FileExists(_path + ".svg"))
			_path += ".svg";
			
		return ResourceLoader.Load<Resource>(_path) as Texture2D;
	}

	// =======| Stat Related Functions |=======
	public virtual void SetHunger(int _amount, bool _setAsCurrent = false)
	{
		if (_setAsCurrent)
			Instance.Status.Hunger = Math.Clamp(_amount, 0, 100);
		else
			Instance.Status.Hunger = Math.Clamp(Instance.Status.Hunger + _amount, 0, 100);
	}
	protected virtual void TickHungerDecay(float _delta)
	{
		if (hunger_decay_timer > 0)
			hunger_decay_timer -= _delta;
		else
		{
			SetHunger(-1);

			hunger_decay_timer = OurState == AphidState.Sleeping ?
				hunger_decay * 2f :
				hunger_decay;
		}
	}
	public virtual void SetThirst(int _amount, bool _setAsCurrent = false)
	{
		if (_setAsCurrent)
			Instance.Status.Thirst = Math.Clamp(_amount, 0, 100);
		else
			Instance.Status.Thirst = Math.Clamp(Instance.Status.Thirst + _amount, 0, 100);
	}
	protected virtual void TickThirstDecay(float _delta)
	{
		if (thirst_decay_timer > 0)
			thirst_decay_timer -= _delta;
		else
		{
			SetThirst(-1);

			thirst_decay_timer = OurState == AphidState.Sleeping ?
				thirst_decay * 2f :
				thirst_decay;
		}
	}
	public virtual void SetBondship(int _amount, bool _setAsCurrent = false)
	{
		if (_setAsCurrent)
			Instance.Status.Bondship = Math.Clamp(_amount, 0, 100);
		else
			Instance.Status.Bondship = Math.Clamp(Instance.Status.Bondship + _amount, 0, 100);
	}
	public virtual void SetAffection(int _amount, bool _setAsCurrent = false)
	{
		if (_setAsCurrent)
			Instance.Status.Affection = Math.Clamp(_amount, 0, 100);
		else
			Instance.Status.Affection = Math.Clamp(Instance.Status.Affection + _amount, 0, 100);
	}
	protected virtual void TickAffectionDecay(float _delta)
	{
		if (OurState == AphidState.Petting)
			return;

		if (affection_decay_timer > 0)
			affection_decay_timer -= _delta;
		else
		{			
			SetAffection(-1);
			affection_decay_timer = 
			OurState == AphidState.Sleeping ?
				affection_decay * 2f :
				affection_decay;
		}
	}
	public virtual void SetSleepiness(float _amount, bool _setAsCurrent = false)
	{
		if (_setAsCurrent)
			Instance.Status.Sleepiness = Math.Clamp(_amount, 0, 100);
		else
			Instance.Status.Sleepiness = Math.Clamp(Instance.Status.Sleepiness + _amount, 0, 100);
	}
	protected virtual void TickSleepDecay(float _delta)
	{
		if (OurState == AphidState.Sleeping)
			return;

		if (sleep_decay_timer > 0)
			sleep_decay_timer -= _delta;
		else
		{			
			SetSleepiness(behaviourRNG.RandfRange(0.5f, 1.5f));
			sleep_decay_timer = sleep_decay;
		}

		// Only if we are tired, then roll a chance every few seconds to sleep
		if (Instance.Status.Sleepiness > 25)
			return;

		if (try_sleep_timer > 0)
			try_sleep_timer -= _delta;
		else if (GameManager.GetRandomByWeight(behaviourRNG, sleep_weights) != 1)
			try_sleep_timer = try_sleep;
		else
			Sleep();
	}
	
	// =======| Collision Behaviours |========
	public void OnTriggerEnter(Node2D _node)
	{
		if (!_node.HasMeta("tag"))
			return;
		var _tag = (string)_node.GetMeta("tag");

		if (TriggerActions.ContainsKey(_tag))
			TriggerActions[_tag](_node);
	}
	protected virtual void OnFoodTrigger(Node2D _node, bool _mode = true)
	{
        switch (OurState) // Anything that is not a valid state, returns
        {
            case AphidState.Idle:
                break;
            case AphidState.Eating:
				if (!_mode)
					return;
                break;
			case AphidState.Drinking:
				if (_mode) // _mode is to reutilize this code for drink items :bigbren: 
					return;
				break;
			default:
				return;
        }

 		// if is now consuming, already pursuing said item, or its marked for ignore, dont bother
		if (gobble_timer > 0 || 
		(food_item != null && food_item.Equals(_node)) || 
		(food_ignore_list.Count > 0 && food_ignore_list.Exists((Node2D _n) => CheckIfIgnore(_n, _node))))
			return;

		var _flavor = (AphidData.FoodType)(int)_node.GetMeta("food_type");
		// if Vile, reject it cause yucky, unless you like it for some reason or are starving
		if (_flavor == AphidData.FoodType.Vile && _flavor != Instance.Genes.FoodPreference && Instance.Status.Hunger > 15)
			return;
		// If not hungy/thirsty, dont bother, unless is your favorite
		var _isfavorite = Instance.Genes.FoodPreference == _flavor;
		if (_mode)
		{
			if (Instance.Status.Hunger > 85 && !_isfavorite)
				return;
		}
		else
		{
			if (Instance.Status.Thirst > 85 && !_isfavorite)
				return;
		}

		// if you are pursuing a food already
		// and the current one is valid
		if (food_item != null && IsInstanceValid(food_item))
		{
			// if either are a favorite or neither, check which one is closer
			// else if this one isnt a favorite, ignore it, this means if it IS, just grab it
			if (_isfavorite == IsFoodFavorite)
			{
				if (GlobalPosition.DistanceTo(_node.GlobalPosition) > GlobalPosition.DistanceTo(food_item.GlobalPosition))
					return;
			}
			else if (!_isfavorite)
				return;
		}

		// Set current food item to pursue
		food_item = _node;
		food_pursue_timer.Start(food_pursue_duration);
		IsFoodFavorite = _isfavorite;
		FoodMode = _mode;

		if (OurState == AphidState.Idle)
			SetAphidState(_mode ? AphidState.Eating : AphidState.Drinking);
    }
	private static bool CheckIfIgnore(Node2D _n, Node2D _node)
	{
		if (!IsInstanceValid(_n))
			return false;
		return _n.Equals(_node);
	}
	protected virtual void OnPlayerTrigger(Node2D _node)
	{
		if (OurState != AphidState.Idle)
			return;

		// cooldown or is waiting to finish current idle loop
		if (interaction_cd_timer > 0 || idle_timer <= 0)
			return;

		interaction_cd_timer = interaction_cd;
		// 0 Nothing : 1 Walk Towards : 2 Flip At It 
		int _interaction = GameManager.GetRandomByWeight(behaviourRNG, player_seek_weights);
		if (_interaction == 1 && Instance.Status.Bondship >= 20)
		{
			idlePosition = _node.GlobalPosition;
			idle_timeout_timer = 0;
		}
		else if (_interaction == 2)
		{
			idlePosition = GlobalPosition;
			SetFlipDirection(_node.GlobalPosition - GlobalPosition);
		}
	}
	private void TickInteractionCooldown(float _delta)
	{
		if (interaction_cd_timer > 0)
			interaction_cd_timer -= _delta;
	}
}