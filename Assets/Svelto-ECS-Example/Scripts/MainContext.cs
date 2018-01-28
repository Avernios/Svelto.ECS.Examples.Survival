using System;
using System.Collections.Generic;
using Svelto.ECS.Example.Survive.Engines.Enemies;
using Svelto.ECS.Example.Survive.Engines.Health;
using Svelto.ECS.Example.Survive.Engines.HUD;
using Svelto.ECS.Example.Survive.Engines.Player;
using Svelto.ECS.Example.Survive.Engines.Player.Gun;
using Svelto.ECS.Example.Survive.Engines.Sound.Damage;
using Svelto.ECS.Example.Survive.Observables.Enemies;
using Svelto.ECS.Example.Survive.Observers.HUD;
using Svelto.Context;
using Svelto.ECS.Example.Survive.CameraImplementors;
using Svelto.ECS.Example.Survive.Engines.Camera;
using Svelto.ECS.Example.Survive.EntityDescriptors.Camera;
using Svelto.ECS.Example.Survive.EntityDescriptors.Player;
using Svelto.ECS.Example.Survive.Implementors.Player;
using Svelto.ECS.Example.Survive.Others;
using UnityEngine;
using Svelto.ECS.Schedulers.Unity;

//Main is the Application Composition Root.
//Composition Root is the place where all the depencies are 
//created and injected (I talk a lot about it in my articles)
//A composition Root belongs to the Context, but
//a Context can have more than a composition root.
//For example a factory is a composition root.
//An Application can also have more than a context
//but this is more advanced and not part of this demo
namespace Svelto.ECS.Example.Survive
{
    /// <summary>
    ///IComposition root is part of Svelto.Context
    ///Svelto.Context is not formally part of Svelto.ECS, but
    ///it's helpful to use in an environment where a Context is
    ///not formally present, like in Unity. 
    /// </summary>
    public class Main : ICompositionRoot
    {
        public Main()
        {
            SetupEnginesAndEntities();
        }

        void SetupEnginesAndEntities()
        {
            //The Engines Root is the core of Svelto.ECS. You must NEVER inject the EngineRoot
            //as it is, however you may inject it as IEntityFactory. In fact, you can build entity
            //inside other engines or factories as well.
            //the UnitySumbmissionEntityViewScheduler is the scheduler that is used by the Root to know
            //when to inject the EntityViews. You shouldn't use a custom one unless you know what you 
            //are doing, so let's assume it's part of the pattern right now.
            _enginesRoot = new EnginesRoot(new UnitySumbmissionEntityViewScheduler());
            //Engines root can never be held by anything else than the context itself to avoid leaks
            //That's why the EntityFactory and EntityFunctions are generated.
            //The EntityFactory can be injected inside factories (or engine acting as factories)
            //to build new entities dynamically
            _entityFactory = _enginesRoot.GenerateEntityFactory();
            //The entity functions is a set of utility operations on Entities, including
            //removing an entity.
            var entityFunctions = _enginesRoot.GenerateEntityFunctions();
            //Allows to create GameObjects without using the Static
            //method GameObject.Instantiate. While it seems a complication
            //it's important to keep the engine testable and not
            //coupled with hard dependencies references (read my articles to understand
            //how dependency injection works and why solving dependencies
            //with static classes and singletons is a terrible mistake)
            GameObjectFactory factory = new GameObjectFactory();
            //The observer pattern is one of the 3 official ways available
            //in Svelto.ECS to communicate. Specifically, Observers should 
            //be used to communicate between engines in very specific cases
            //as it's not the preferred solution and to communicate beteween
            //engines and legacy code/third party code.
            //Use them carefully.
            //Observers and Observables should be named according where they are 
            //used. Observer and Observable are decoupled to allow each object
            //to be used in the relative context which promote separation of concerns.
            var enemyKilledObservable = new EnemyKilledObservable();
            var scoreOnEnemyKilledObserver = new ScoreOnEnemyKilledObserver(enemyKilledObservable);
            //the Sequencer is one of the 3 official ways available in Svelto.ECS 
            //to communicate. They are mainly used for two specific cases:
            //1) specify a strict execution order between engines (engine logic
            //is executed horizontally instead than vertically, I will talk about this
            //in my articles). 2) filter a data token passed as parameter through
            //engines. The Sequencer is also not the common way to communicate
            //between engines
            Sequencer playerDamageSequence = new Sequencer();
            Sequencer enemyDamageSequence = new Sequencer();
            //Player related engines. ALL the dependecies must be solved at this point
            //through constructor injection.
            var playerHealthEngine = new HealthEngine(entityFunctions, playerDamageSequence);
            var playerShootingEngine = new PlayerGunShootingEngine(enemyKilledObservable, enemyDamageSequence);
            var playerMovementEngine = new PlayerMovementEngine();
            var playerAnimationEngine = new PlayerAnimationEngine();
            //Enemy related engines
            var enemyAnimationEngine = new EnemyAnimationEngine();
            var enemyHealthEngine = new HealthEngine(entityFunctions, enemyDamageSequence);
            var enemyAttackEngine = new EnemyAttackEngine(playerDamageSequence);
            var enemyMovementEngine = new EnemyMovementEngine();
            //hud and sound engines
            var hudEngine = new HUDEngine();
            var damageSoundEngine = new DamageSoundEngine();
            var enemySpawnerEngine = new EnemySpawnerEngine(factory, _entityFactory);

            //The Sequencer implementaton is very simple, but allows to perform
            //complex concatenation including loops and conditional branching.
            playerDamageSequence.SetSequence(
                new Steps //sequence of steps, this is a dictionary!
                { 
                    { //first step
                        enemyAttackEngine, //this step can be triggered only by this engine through the Next function
                        new To //this step can lead only to one branch
                        { 
                            playerHealthEngine, //this is the only engine that will be called when enemyAttackEngine triggers Next()
                        }  
                    },
                    { //second step
                        playerHealthEngine, //this step can be triggered only by this engine through the Next function
                        new To //this step can branch in two paths
                        { 
                            {  DamageCondition.damage, new IStep[] { hudEngine, damageSoundEngine }  }, //these engines will be called when the Next function is called with the DamageCondition.damage set
                            {  DamageCondition.dead, new IStep[] { hudEngine, damageSoundEngine, 
                                playerMovementEngine, playerAnimationEngine, enemyAnimationEngine }  }, //these engines will be called when the Next function is called with the DamageCondition.dead set
                        }  
                    }  
                }
            );

            enemyDamageSequence.SetSequence(
                new Steps
                { 
                    { 
                        playerShootingEngine, 
                        new To
                        { 
                            enemyHealthEngine,
                        }  
                    },
                    { 
                        enemyHealthEngine, 
                        new To
                        { 
                            {  DamageCondition.damage, new IStep[] { enemyAnimationEngine }  },
                            {  DamageCondition.dead, new IStep[] { enemyMovementEngine, enemyAnimationEngine, playerShootingEngine, enemySpawnerEngine }  },
                        }  
                    }  
                }
            );

            //Mandatory step to make engines work
            //Player engines
            _enginesRoot.AddEngine(playerMovementEngine);
            _enginesRoot.AddEngine(playerAnimationEngine);
            _enginesRoot.AddEngine(playerShootingEngine);
            _enginesRoot.AddEngine(playerHealthEngine);
            _enginesRoot.AddEngine(new PlayerInputEngine());
            _enginesRoot.AddEngine(new PlayerGunShootingFXsEngine());
            //enemy engines
            _enginesRoot.AddEngine(enemySpawnerEngine);
            _enginesRoot.AddEngine(enemyAttackEngine);
            _enginesRoot.AddEngine(enemyMovementEngine);
            _enginesRoot.AddEngine(enemyAnimationEngine);
            _enginesRoot.AddEngine(enemyHealthEngine);
            //other engines
            _enginesRoot.AddEngine(new CameraFollowTargetEngine());
            _enginesRoot.AddEngine(damageSoundEngine);
            _enginesRoot.AddEngine(hudEngine);
            _enginesRoot.AddEngine(new ScoreEngine(scoreOnEnemyKilledObserver));
        }
        
        /// <summary>
        /// This is a standard approach to create Entities from already existing GameObject in the scene
        /// It is absolutely not necessary, but convienent in case you prefer this way
        /// </summary>
        /// <param name="contextHolder"></param>
        void ICompositionRoot.OnContextCreated(UnityContext contextHolder)
        {
            var prefabsDictionary = new PrefabsDictionary(Application.persistentDataPath + "/prefabs.json");
                
            BuildEntitiesFromScene(contextHolder);
            //Entities can also be created dynamically in run-time
            //using the entityFactory; You can, if you wish, create
            //starting entities here.
            BuildPlayerEntities(prefabsDictionary);
            BuildCameraEntity();
        }

        void BuildPlayerEntities(PrefabsDictionary prefabsDictionary)
        {
            //Building entities dynamically should be always preferred
            //and MUST be used if an implementor doesn't need to be
            //a Monobehaviour. You should strive to create implementors
            //not as monobehaviours. Implementors as monobehaviours 
            //are meant only to function as bridge between Svelto.ECS
            //and Unity3D. Using implementor as monobehaviour
            //just to read serialized data from the editor, is also
            //a bad practice, use a Json file instead.
            var player = prefabsDictionary.Istantiate("Player");
            
            List<IImplementor> implementors = new List<IImplementor>();
            //fetching implementors as monobehaviours, used as bridge between
            //Svelto.ECS and Unity3D
            player.GetComponents(implementors);
            //Add not monobehaviour implementors
            implementors.Add(new PlayerInputImplementor());
            
            _entityFactory.BuildEntity<PlayerEntityDescriptor>(player.GetInstanceID(), implementors.ToArray());

            //unluckily the gun is parented in the original prefab, so there is no easy way to create it
            //explicitly, I have to create if from the existing gameobject.
            var gun = player.GetComponentInChildren<PlayerShootingImplementor>();
            _entityFactory.BuildEntity<PlayerGunEntityDescriptor>(gun.gameObject.GetInstanceID(),  new object[] {gun});
        }

        void BuildCameraEntity()
        {
            var implementor = Camera.main.gameObject.AddComponent<CameraImplementor>();

            _entityFactory.BuildEntity<CameraEntityDescriptor>(Camera.main.GetInstanceID(), new object[] {implementor});
        }

        void BuildEntitiesFromScene(UnityContext contextHolder)
        {
            //An EntityDescriptorHolder is a special Svelto.ECS class created to exploit
            //GameObjects to dynamically retrieve the Entity information attached to it.
            //Basically a GameObject can be used to hold all the information needed to create
            //an Entity and later queries to build the entitity itself.
            //This allow to trigger a sort of polyformic code that can be re-used to 
            //create several type of entities.
            IEntityDescriptorHolder[] entities = contextHolder.GetComponentsInChildren<IEntityDescriptorHolder>();

            for (int i = 0; i < entities.Length; i++)
            {
                var entityDescriptorHolder = entities[i];
                var entityDescriptor = entityDescriptorHolder.RetrieveDescriptor();
                _entityFactory.BuildEntity
                (((MonoBehaviour) entityDescriptorHolder).gameObject.GetInstanceID(),
                    entityDescriptor,
                    (entityDescriptorHolder as MonoBehaviour).GetComponentsInChildren<IImplementor>());
            }
        }

        //part of Svelto.Context
        void ICompositionRoot.OnContextInitialized()
        {}
        
        //part of Svelto.Context
        void ICompositionRoot.OnContextDestroyed()
        {   //final clean up
            _enginesRoot.Dispose();
            
            //Tasks can run across level loading, so if you don't want
            //that, the runners must be stopped explicitily.
            //carefull because if you don't do it and 
            //unintentionally leave tasks running, you will cause leaks
            TaskRunner.Instance.StopAndCleanupAllDefaultSchedulerTasks();
        }

        EnginesRoot    _enginesRoot;
        IEntityFactory _entityFactory;
 }

    /// <summary>
    ///At least One GameObject containing a UnityContext must be present in the scene.
    ///All the monobehaviours existing in gameobjects child of the UnityContext one, 
    ///can be later queried, usually to create entities from statically created
    ///gameobjects. 
    /// </summary>
    public class MainContext : UnityContext<Main>
    { }

}