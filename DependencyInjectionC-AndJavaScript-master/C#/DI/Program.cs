using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using System.Reflection;

namespace DI
{
    class Program
    {
        static void Main(string[] args)
        {
            //Create the person class
            //Save the person info

            String personDataManagerTypeString = ConfigurationManager.AppSettings["PersonDataManagerType"];
            Type personDataManagerType = Type.GetType(String.Format("DI.{0}", personDataManagerTypeString), true);
            IPersonDataManager personDataManager = Activator.CreateInstance(personDataManagerType) as IPersonDataManager;

            Person person = new Person() { FirstName = "John", LastName = "Doe" };
            //IPersonDataManager personDataManager = new PersonDataManagerSqlDB();
            //PersonManager personManager = new PersonManager(person, personDataManager);

            //Proprety Injection
            //PersonManager personManager = new PersonManager(person);
            //personManager.PersonDataManager = personDataManager;

            //Setter Function Injection
            //PersonManager personManager = new PersonManager(person);
            //personManager.SetPersonDataManager(personDataManager);

            //Interface Injection
            PersonManager personManager = new PersonManager(person);
            personManager.SetPersonDataManagerObject(personDataManager);

            //Save the person info
            personManager.SavePerson();

            
            
            

            Console.ReadLine();
            //SavePerson(person);
        }

        static void SavePerson(Person person)
        {
            //save person info
        }
    }

    class Person
    {
        public String FirstName { get; set; }
        public String LastName { get; set; }
    }

    #region Person Data Manager
    

    interface IPersonDataManager
    {
        void SavePerson(Person person);
    }

    class PersonDataManagerTextFile : IPersonDataManager
    {
        public void SavePerson(Person person)
        {
            Console.WriteLine("Person details are saved to a text file");
        }
    }

    class PersonDataManagerXmlFile : IPersonDataManager
    {
        public void SavePerson(Person person)
        {
            Console.WriteLine("Person details are saved to a xml file");
        }
    }

    class PersonDataManagerSqlDB : IPersonDataManager
    {
        public void SavePerson(Person person)
        {
            Console.WriteLine("Person details are saved to a sql db");
        }
    }

    #endregion Person Data Manager

    interface IPersonManagerInjector
    {
        void SetPersonDataManagerObject(IPersonDataManager personDataManager);
    }

    class PersonManager : IPersonManagerInjector
    {
        private Person _person;
        private IPersonDataManager _personDataManager;

        //public IPersonDataManager PersonDataManager
        //{
        //    get { return _personDataManager; }
        //    set { _personDataManager = value; }
        //}
        //private IPersonDataManager _personDataManager;

        public void SetPersonDataManager(IPersonDataManager personDataManager)
        {
            _personDataManager = personDataManager;
        }

        public void SetPersonDataManagerObject(IPersonDataManager personDataManager)
        {
            _personDataManager = personDataManager;
        }

        //public PersonManager(Person person, IPersonDataManager personDataManager)
        //{
        //    _person = person;
        //    _personDataManager = personDataManager;
        //}

        public PersonManager(Person person)
        {
            _person = person;
        }

        public void SavePerson()
        {
            _personDataManager.SavePerson(_person);
        }
    }
}