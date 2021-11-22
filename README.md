# HandlerFinder

This is an extension for Visual Studio to help finding Command/Request Handlers for MediatR.

## Usage
- Right click on a type, class definition or record to open up the context menu
- Click "Find handler"
- If there is a handler, visual studio will navigate to it
- In case it was not able to find a handler it will not open anything (maybe there will be better feedback in the future)

## Logic
- It first decides, whether to look for a command handler or a request handler
- According to the handlers type, it will get a reference to the containing project
  - For command handlers it will get the domain project
  - For request/query handlers it will get the application project
- After that, it will look for *.cs files contained in the following folders
  - "commandhandlers"
  - "commandhandler"
  - "queryhandlers"
  - "queryhandler"
- Then it will look for classes that implement `IRequestHandler`
- Iterate over all `IRequestHandler` and determine the first type argument
- If the type argument matches the request handlers name, it was successful
- The filename and the line position will be determined
- Visual studio opens the file and will goto the line where the handler method is defined

## Notes
- It also works for records
- It also works for class definitions
