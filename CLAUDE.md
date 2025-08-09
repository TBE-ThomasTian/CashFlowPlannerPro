# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CashflowPlannerProCpp is a Qt6-based desktop application for personal cashflow planning and management. It provides financial tracking through transactions, offers, invoices, and targets with visualization capabilities.

## Architecture

### Core Components

- **App** (`src/App.h/cpp`): Main application entry point that creates and manages the MainWindow
- **MainWindow** (`src/MainWindow.h/cpp`): Central widget containing tabbed interface for all pages
- **Database** (`src/Database.h/cpp`): Singleton pattern database manager handling SQLite operations and schema management

### Feature Pages

- **Dashboard** (`src/Dashboard.h/cpp`): Financial overview with charts, NPV/IRR calculations, and cashflow projections
- **TransactionsPage** (`src/TransactionsPage.h/cpp`): Manage income/expense transactions with recurring support
- **OffersPage** (`src/OffersPage.h/cpp`): Track potential deals with probability weighting
- **InvoicesPage** (`src/InvoicesPage.h/cpp`): Invoice management with payment tracking
- **TargetsPage** (`src/TargetsPage.h/cpp`): Set and monitor monthly financial targets

### Database Schema

Tables defined in `sql/migrations/001_init.sql`:
- `transactions`: Core financial records with category/person associations and recurring intervals
- `offers`: Potential deals with probability and expected dates
- `invoices`: Customer invoices with payment tracking
- `targets`: Monthly financial goals
- `categories`, `persons`: Reference tables for transaction classification
- `settings`: Key-value configuration storage

## Build System

### Prerequisites
- Qt6 (6.2+) with Widgets, Sql, and Charts modules
- CMake 3.21+
- C++17 compiler

### Build Commands

```bash
# Configure build (from project root)
cmake -B build -DCMAKE_BUILD_TYPE=Debug
# or for Qt Creator integration:
cmake -B build/Desktop_Qt_6_9_1-Debug -DCMAKE_BUILD_TYPE=Debug

# Build project
cmake --build build
# or with ninja:
cd build && ninja

# Run application
./build/CashflowPlannerProCpp
```

### Development Notes

- Build system uses Ninja generator (as seen in `build/Desktop_Qt_6_9_1-Debug/`)
- Resources (styles, SQL migrations) are automatically copied to build directory
- Compact code style without unnecessary whitespace or comments
- All database operations go through the Database singleton
- Qt Charts used for financial visualizations on Dashboard