using System;
using System.Collections.Generic;

namespace GameSample
{
    public class PlayerController
    {
        // Fields
        private int _health;
        private float _speed;
        public static int MaxPlayers = 4;
        private readonly string _name;
        private List<string> _inventory;

        // Properties
        public int Health { get; set; }
        public string Name { get { return _name; } }
        public bool IsAlive { get; private set; }

        // Constructor
        public PlayerController(string name, int health)
        {
            _name = name;
            _health = health;
            IsAlive = true;
        }

        // Methods
        public void TakeDamage(int amount)
        {
            _health -= amount;
            if (_health <= 0)
            {
                IsAlive = false;
            }
        }

        public int GetHealth()
        {
            return _health;
        }

        private string GetStatus()
        {
            return IsAlive ? "Alive" : "Dead";
        }

        public static PlayerController Create(string name, int health)
        {
            return new PlayerController(name, health);
        }

        public List<string> GetInventory()
        {
            return _inventory;
        }

        protected virtual bool TryPickup(string itemName, int quantity = 1)
        {
            if (_inventory == null) return false;
            _inventory.Add(itemName);
            return true;
        }

        public async Task<bool> SaveAsync(string filePath)
        {
            // async method
            return await Task.FromResult(true);
        }

        public Dictionary<string, int> GetStats()
        {
            return new Dictionary<string, int>
            {
                { "health", _health }
            };
        }
    }
}
