# Dbase storage versioning tool

**Dbase** is a tool for keeping a remote server data storage (*mssql*, *postgresql*. etc.) up to date with versioned script files. Scripts can be either in a native to your server format, or in *dbase universal scripts* format

> Dbase is a universal crossplatform tool and can work with any data storage, but for now only the *MSSQL* and *PostgreSQL* processors are implemented

With [universal script files](https://github.com/vagrod/dbase/wiki/Universal-script-files) you can manage storage structure and do simple data operations in a simple *yaml* form, and those scripts will be translated into the final sql by the selected processor, and then will be executed on a database.

`VSCode` and `Sublime Text` syntax highlighters are available (in the `misc` folder).

``` yaml
storage:
  add:
  - name: Users                                   desc Users cache
    fields:
      Id: uuid key                                desc User Id
      Identity: string-100 required               desc User identity
      UniqueNumber: big unique, required          desc Unique number
      DisplayName: string-100 required            desc User name
      Position: string-150 required               desc User position
    order: 1

  alter:
  - name: Employees
    fields:
      LinkedUser: add uuid fk Users.Id            desc Optional link to a user
    order: 2

data:
  alter:
  - storage: Employees
    fields:
      LinkedUser: '''2248e110-cadc-4804-bee9-7ebc797a99f4'''
    clause: Employees.Id = '''2248e110-cadc-4804-bee9-7ebc797a99f4'''
    order: 3
```

Every script has its version, and **dbase** carefully tracks what scripts have already been execured on a source, and what -- haven't, and will execure them for you, so your databases are always up-to-date across all your environments -- with a simple command:

```
dbase -run DATABASE_NAME prod "path\to\folder\with\scripts"
```

Of course, universal scripts are only an option -- you can use scripts that are native to your database as well, or you can mix them.

Refer to [wiki](https://github.com/vagrod/dbase/wiki) for more info.
