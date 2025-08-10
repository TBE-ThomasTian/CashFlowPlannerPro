cmake_minimum_required(VERSION 3.21)
project(CashflowPlannerProCpp LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)
find_package(Qt6 6.2 REQUIRED COMPONENTS Widgets Sql Charts)
qt6_standard_project_setup()

# Qt Resources
qt6_add_resources(RESOURCES resources.qrc)

# Set up sources
set(SOURCES
    src/main.cpp
    src/App.cpp src/App.h
    src/MainWindow.cpp src/MainWindow.h
    src/Database.cpp src/Database.h
    src/Dashboard.cpp src/Dashboard.h
    src/TransactionsPage.cpp src/TransactionsPage.h
    src/OffersPage.cpp src/OffersPage.h
    src/InvoicesPage.cpp src/InvoicesPage.h
    src/TargetsPage.cpp src/TargetsPage.h
    src/TaxesPage.cpp src/TaxesPage.h
    src/FixkostenPage.cpp src/FixkostenPage.h
    src/LoginDialog.cpp src/LoginDialog.h
    src/TableDelegates.h
    ${RESOURCES}
)

# Add Windows resource file for icon
if(WIN32)
    set(SOURCES ${SOURCES} resources/app.rc)
endif()

add_executable(${PROJECT_NAME} ${SOURCES})
target_include_directories(${PROJECT_NAME} PRIVATE src)
target_link_libraries(${PROJECT_NAME} Qt6::Widgets Qt6::Sql Qt6::Charts)
add_custom_target(copy_resources ALL
    COMMAND ${CMAKE_COMMAND} -E copy_directory ${CMAKE_SOURCE_DIR}/resources ${CMAKE_BINARY_DIR}/resources
    COMMAND ${CMAKE_COMMAND} -E copy_directory ${CMAKE_SOURCE_DIR}/sql ${CMAKE_BINARY_DIR}/sql
)
add_dependencies(${PROJECT_NAME} copy_resources)
