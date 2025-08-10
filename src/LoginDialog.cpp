#include "LoginDialog.h"
#include "Database.h"
#include <QSettings>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QLineEdit>
#include <QPushButton>
#include <QLabel>
#include <QComboBox>
#include <QPixmap>
#include <QSqlQuery>
#include <QSqlError>
#include <QMessageBox>
#include <QCryptographicHash>
#include <QTimer>
#include <QFrame>
#include <QDialog>
#include <QVariant>
#include <QCheckBox>
#include <QStringList>
#include <QToolButton>
#include <QFileDialog>
#include <QGraphicsDropShadowEffect>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QMouseEvent>
#include <QScreen>
#include <QApplication>
#include <QIcon>

LoginDialog::LoginDialog(QWidget *parent) : QDialog(parent) {
    setWindowTitle("Cashflow Planer - Anmeldung");
    setFixedSize(1000, 650);
    // Use standard window with title bar
    setWindowFlags(Qt::Dialog);
    
    // Window icon is set in main.cpp for the whole application
    
    // Load last database path from settings (cross-platform)
    QSettings settings("CashflowPlanner", "CashflowPlannerPro");
    QString lastDbPath = settings.value("lastDatabasePath", "").toString();
    
    // Set database path - use last path if it exists, otherwise default
    if (!lastDbPath.isEmpty() && QFile::exists(lastDbPath)) {
        m_databasePath = lastDbPath;
    } else {
        m_databasePath = QDir::currentPath() + "/cashflow.db";
    }
    
    // Create main widget
    auto *mainWidget = new QWidget(this);
    mainWidget->setObjectName("loginWidget");
    mainWidget->setStyleSheet(
        "#loginWidget {"
        "    background-color: #ffffff;"
        "}"
    );
    
    // Main layout
    auto *outerLayout = new QVBoxLayout(this);
    outerLayout->setContentsMargins(0, 0, 0, 0);
    outerLayout->addWidget(mainWidget);
    
    // Create horizontal split layout
    auto *splitLayout = new QHBoxLayout(mainWidget);
    splitLayout->setContentsMargins(0, 0, 0, 0);
    splitLayout->setSpacing(0);
    
    // LEFT SIDE - Login Form
    auto *leftWidget = new QWidget(mainWidget);
    leftWidget->setStyleSheet(
        "background-color: #ffffff;"
    );
    leftWidget->setFixedWidth(500);
    
    auto *leftLayout = new QVBoxLayout(leftWidget);
    leftLayout->setSpacing(15);
    leftLayout->setContentsMargins(50, 60, 50, 50);
    
    // RIGHT SIDE - Decorative Panel
    auto *rightWidget = new QWidget(mainWidget);
    rightWidget->setStyleSheet(
        "background-color: #f8f9fa;"
    );
    
    auto *rightLayout = new QVBoxLayout(rightWidget);
    rightLayout->setAlignment(Qt::AlignCenter);
    rightLayout->setContentsMargins(40, 40, 40, 40);
    
    splitLayout->addWidget(leftWidget);
    splitLayout->addWidget(rightWidget, 1);
    
    // === LEFT SIDE CONTENT ===
    
    auto *subtitleLabel = new QLabel("Melden Sie sich bei Ihrem Konto an");
    subtitleLabel->setStyleSheet(
        "font-size: 12px;"
        "color: #6c757d;"
        "margin-bottom: 15px;"
    );
    leftLayout->addWidget(subtitleLabel);
    
    // Database selection section
    auto *dbSectionLabel = new QLabel("<b>1. Datenbank wählen</b>", leftWidget);
    dbSectionLabel->setStyleSheet(
        "color: #212529;"
        "font-size: 13px;"
        "margin-bottom: 8px;"
    );
    leftLayout->addWidget(dbSectionLabel);
    
    // Database path display
    auto *dbContainer = new QWidget(leftWidget);
    auto *dbLayout = new QHBoxLayout(dbContainer);
    dbLayout->setContentsMargins(0, 0, 0, 0);
    dbLayout->setSpacing(8);
    
    m_dbPathLabel = new QLabel(m_databasePath, dbContainer);
    m_dbPathLabel->setStyleSheet(
        "QLabel {"
        "    background-color: #f8f9fa;"
        "    border: 2px solid #e9ecef;"
        "    border-radius: 8px;"
        "    padding: 8px 12px;"
        "    font-size: 11px;"
        "    color: #495057;"
        "    font-family: monospace;"
        "}"
    );
    m_dbPathLabel->setWordWrap(true);
    
    auto *dbButtonsWidget = new QWidget(dbContainer);
    auto *dbButtonsLayout = new QHBoxLayout(dbButtonsWidget);
    dbButtonsLayout->setContentsMargins(0, 0, 0, 0);
    dbButtonsLayout->setSpacing(4);
    
    auto *selectDbButton = new QToolButton(dbButtonsWidget);
    selectDbButton->setText("📁");
    selectDbButton->setToolTip("Andere Datenbank öffnen");
    selectDbButton->setCursor(Qt::PointingHandCursor);
    selectDbButton->setStyleSheet(
        "QToolButton {"
        "    background-color: #ffffff;"
        "    border: 2px solid #e9ecef;"
        "    border-radius: 8px;"
        "    padding: 6px 10px;"
        "    font-size: 14px;"
        "}"
        "QToolButton:hover {"
        "    background-color: #f8f9fa;"
        "    border-color: #64849a;"
        "}"
    );
    connect(selectDbButton, &QToolButton::clicked, this, &LoginDialog::selectDatabase);
    
    auto *newDbButton = new QToolButton(dbButtonsWidget);
    newDbButton->setText("➕");
    newDbButton->setToolTip("Neue Datenbank erstellen");
    newDbButton->setCursor(Qt::PointingHandCursor);
    newDbButton->setStyleSheet(selectDbButton->styleSheet());
    connect(newDbButton, &QToolButton::clicked, this, &LoginDialog::createNewDatabase);
    
    dbButtonsLayout->addWidget(selectDbButton);
    dbButtonsLayout->addWidget(newDbButton);
    
    dbLayout->addWidget(m_dbPathLabel, 1);
    dbLayout->addWidget(dbButtonsWidget);
    leftLayout->addWidget(dbContainer);
    
    leftLayout->addSpacing(10);
    
    // Login section
    auto *loginSectionLabel = new QLabel("<b>2. Anmelden</b>", leftWidget);
    loginSectionLabel->setStyleSheet(
        "color: #212529;"
        "font-size: 13px;"
        "margin-bottom: 8px;"
    );
    leftLayout->addWidget(loginSectionLabel);
    
    // Username field
    auto *userLabel = new QLabel("Benutzername", leftWidget);
    userLabel->setStyleSheet(
        "color: #495057;"
        "font-size: 13px;"
        "font-weight: 500;"
        "margin-bottom: 8px;"
    );
    leftLayout->addWidget(userLabel);
    
    m_userCombo = new QComboBox(leftWidget);
    m_userCombo->setEditable(true);
    m_userCombo->setPlaceholderText("name@beispiel.de");
    m_userCombo->setStyleSheet(
        "QComboBox {"
        "    background-color: #f8f9fa;"
        "    border: 2px solid #e9ecef;"
        "    border-radius: 8px;"
        "    padding: 10px 14px;"
        "    font-size: 14px;"
        "    color: #212529;"
        "}"
        "QComboBox:hover {"
        "    border: 2px solid #dee2e6;"
        "    background-color: #ffffff;"
        "}"
        "QComboBox:focus {"
        "    border: 2px solid #64849a;"
        "    background-color: #ffffff;"
        "    outline: none;"
        "}"
        "QComboBox::drop-down {"
        "    border: none;"
        "    width: 30px;"
        "}"
        "QComboBox::down-arrow {"
        "    image: none;"
        "    border-left: 5px solid transparent;"
        "    border-right: 5px solid transparent;"
        "    border-top: 6px solid #6c757d;"
        "    margin-right: 5px;"
        "}"
    );
    // Initialize database and load users
    if (Database::instance().open(m_databasePath)) {
        Database::instance().ensureSchema();
    }
    updateUserList();
    leftLayout->addWidget(m_userCombo);
    
    leftLayout->addSpacing(5);
    
    // Password field
    auto *passLabel = new QLabel("Passwort", leftWidget);
    passLabel->setStyleSheet(
        "color: #495057;"
        "font-size: 13px;"
        "font-weight: 500;"
        "margin-bottom: 8px;"
    );
    leftLayout->addWidget(passLabel);
    
    m_passwordEdit = new QLineEdit(leftWidget);
    m_passwordEdit->setPlaceholderText("••••••••••••");
    m_passwordEdit->setEchoMode(QLineEdit::Password);
    m_passwordEdit->setStyleSheet(
        "QLineEdit {"
        "    background-color: #f8f9fa;"
        "    border: 2px solid #e9ecef;"
        "    border-radius: 8px;"
        "    padding: 10px 14px;"
        "    font-size: 14px;"
        "    color: #212529;"
        "}"
        "QLineEdit:hover {"
        "    border: 2px solid #dee2e6;"
        "    background-color: #ffffff;"
        "}"
        "QLineEdit:focus {"
        "    border: 2px solid #64849a;"
        "    background-color: #ffffff;"
        "    outline: none;"
        "}"
    );
    leftLayout->addWidget(m_passwordEdit);
    
    // Hidden username field for internal use
    m_usernameEdit = new QLineEdit(leftWidget);
    m_usernameEdit->setVisible(false);
    connect(m_userCombo, &QComboBox::currentTextChanged, [this](const QString &text) {
        m_usernameEdit->setText(text);
    });
    
    // Message label
    m_messageLabel = new QLabel(leftWidget);
    m_messageLabel->setStyleSheet(
        "color: #dc3545;"
        "font-size: 13px;"
        "background-color: #f8d7da;"
        "padding: 10px 14px;"
        "border-radius: 8px;"
        "border: 1px solid #f5c6cb;"
    );
    m_messageLabel->setAlignment(Qt::AlignCenter);
    m_messageLabel->setWordWrap(true);
    m_messageLabel->setVisible(false);
    leftLayout->addWidget(m_messageLabel);
    
    leftLayout->addSpacing(20);
    
    // Login button
    m_loginButton = new QPushButton("Anmelden", leftWidget);
    m_loginButton->setDefault(true);
    m_loginButton->setCursor(Qt::PointingHandCursor);
    m_loginButton->setStyleSheet(
        "QPushButton {"
        "    background-color: #64849a;"
        "    color: white;"
        "    border: none;"
        "    padding: 14px;"
        "    border-radius: 10px;"
        "    font-size: 14px;"
        "    font-weight: 600;"
        "}"
        "QPushButton:hover {"
        "    background-color: #536d82;"
        "}"
        "QPushButton:pressed {"
        "    background-color: #4a5f73;"
        "}"
    );
    leftLayout->addWidget(m_loginButton);
    
    leftLayout->addStretch();
    
    // === RIGHT SIDE CONTENT ===
    
    // Logo with file selection capability
    auto *logoContainer = new QWidget(rightWidget);
    auto *logoLayout = new QVBoxLayout(logoContainer);
    logoLayout->setAlignment(Qt::AlignCenter);
    logoLayout->setSpacing(10);
    
    m_logoLabel = new QLabel(logoContainer);
    m_logoLabel->setAlignment(Qt::AlignCenter);
    m_logoLabel->setFixedSize(280, 280);
    m_logoLabel->setStyleSheet(
        "QLabel {"
        "    background-color: #ffffff;"
        "    border: 2px solid #e9ecef;"
        "    border-radius: 20px;"
        "    padding: 30px;"
        "}"
    );
    
    // Load logo - check for custom logo first
    QPixmap logo;
    QSqlQuery logoQuery(Database::instance().db());
    logoQuery.exec("SELECT value FROM settings WHERE key = 'custom_logo_path'");
    if (logoQuery.next()) {
        m_customLogoPath = logoQuery.value(0).toString();
        logo = QPixmap(m_customLogoPath);
    }
    
    if (logo.isNull()) {
        logo = QPixmap("resources/CashFlowLoginIcon.png");
    }
    
    if (!logo.isNull()) {
        m_logoLabel->setPixmap(logo.scaled(220, 220, Qt::KeepAspectRatio, Qt::SmoothTransformation));
    } else {
        m_logoLabel->setText("💰");
        m_logoLabel->setStyleSheet(m_logoLabel->styleSheet() + "font-size: 120px;");
    }
    
    logoLayout->addWidget(m_logoLabel);
    rightLayout->addWidget(logoContainer);
    
    // Title for right side
    auto *rightTitle = new QLabel("Cashflow Planer", rightWidget);
    rightTitle->setAlignment(Qt::AlignCenter);
    rightTitle->setStyleSheet(
        "font-size: 28px;"
        "font-weight: 700;"
        "color: #212529;"
        "margin-top: 20px;"
    );
    rightLayout->addWidget(rightTitle);
    
    auto *rightSubtitle = new QLabel("Verwalten Sie Ihre Finanzen\nmit Leichtigkeit", rightWidget);
    rightSubtitle->setAlignment(Qt::AlignCenter);
    rightSubtitle->setStyleSheet(
        "font-size: 16px;"
        "color: #6c757d;"
        "margin-bottom: 20px;"
    );
    rightLayout->addWidget(rightSubtitle);
    
    rightLayout->addStretch();
    
    // Connect signals
    connect(m_loginButton, &QPushButton::clicked, this, &LoginDialog::onLoginClicked);
    connect(m_passwordEdit, &QLineEdit::returnPressed, this, &LoginDialog::onLoginClicked);
    
    // Set focus
    m_passwordEdit->setFocus();
}

void LoginDialog::updateUserList() {
    m_userCombo->clear();
    
    QSqlQuery query(Database::instance().db());
    query.exec("SELECT username, full_name FROM users ORDER BY username");
    
    while (query.next()) {
        QString username = query.value(0).toString();
        QString fullName = query.value(1).toString();
        QString displayText = username;
        if (!fullName.isEmpty()) {
            displayText += " (" + fullName + ")";
        }
        m_userCombo->addItem(displayText, username);
    }
}

void LoginDialog::onLoginClicked() {
    QString username = m_userCombo->currentText();
    QString password = m_passwordEdit->text();
    
    // Extract username if combo shows "username (full name)"
    if (username.contains("(")) {
        username = username.left(username.indexOf("(")).trimmed();
    }
    
    if (username.isEmpty() || password.isEmpty()) {
        m_messageLabel->setText("Bitte alle Felder ausfüllen");
        m_messageLabel->setVisible(true);
        return;
    }
    
    // Check credentials
    QSqlQuery query(Database::instance().db());
    query.prepare("SELECT id, full_name FROM users WHERE username = ? AND password_hash = ?");
    query.addBindValue(username);
    query.addBindValue(password);  // In production, hash the password!
    
    if (query.exec() && query.next()) {
        // Login successful - save current database path
        QSettings settings("CashflowPlanner", "CashflowPlannerPro");
        settings.setValue("lastDatabasePath", m_databasePath);
        accept();
    } else {
        m_messageLabel->setText("Ungültige Anmeldedaten");
        m_messageLabel->setVisible(true);
        m_passwordEdit->clear();
        m_passwordEdit->setFocus();
        
        // Hide message after 3 seconds
        QTimer::singleShot(3000, [this]() {
            m_messageLabel->setVisible(false);
        });
    }
}

void LoginDialog::onRegisterClicked() {
    // Create a simple registration dialog
    QDialog registerDialog(this);
    registerDialog.setWindowTitle("Neuen Benutzer anlegen");
    registerDialog.setFixedSize(400, 250);
    
    auto *layout = new QVBoxLayout(&registerDialog);
    layout->setSpacing(15);
    layout->setContentsMargins(30, 30, 30, 30);
    
    auto *infoLabel = new QLabel("Erstellen Sie einen neuen Benutzer für diese Datenbank:");
    layout->addWidget(infoLabel);
    
    auto *usernameEdit = new QLineEdit();
    usernameEdit->setPlaceholderText("Benutzername");
    usernameEdit->setStyleSheet(
        "QLineEdit {"
        "    padding: 10px;"
        "    border: 2px solid #E8ECEF;"
        "    border-radius: 5px;"
        "    font-size: 14px;"
        "}"
    );
    layout->addWidget(usernameEdit);
    
    auto *passwordEdit = new QLineEdit();
    passwordEdit->setPlaceholderText("Passwort");
    passwordEdit->setEchoMode(QLineEdit::Password);
    passwordEdit->setStyleSheet(usernameEdit->styleSheet());
    layout->addWidget(passwordEdit);
    
    auto *fullNameEdit = new QLineEdit();
    fullNameEdit->setPlaceholderText("Vollständiger Name (optional)");
    fullNameEdit->setStyleSheet(usernameEdit->styleSheet());
    layout->addWidget(fullNameEdit);
    
    auto *buttonLayout = new QHBoxLayout();
    auto *cancelButton = new QPushButton("Abbrechen");
    auto *createButton = new QPushButton("Benutzer anlegen");
    createButton->setStyleSheet(
        "QPushButton {"
        "    background-color: #64849a;"
        "    color: white;"
        "    border: none;"
        "    padding: 10px 20px;"
        "    border-radius: 8px;"
        "    font-weight: 600;"
        "}"
        "QPushButton:hover {"
        "    background-color: #536d82;"
        "}"
    );
    
    buttonLayout->addWidget(cancelButton);
    buttonLayout->addWidget(createButton);
    layout->addLayout(buttonLayout);
    
    connect(cancelButton, &QPushButton::clicked, &registerDialog, &QDialog::reject);
    connect(createButton, &QPushButton::clicked, [&]() {
        QString username = usernameEdit->text();
        QString password = passwordEdit->text();
        QString fullName = fullNameEdit->text();
        
        if (username.isEmpty() || password.isEmpty()) {
            QMessageBox::warning(&registerDialog, "Fehler", "Benutzername und Passwort sind erforderlich!");
            return;
        }
        
        // Check if user exists
        QSqlQuery checkQuery(Database::instance().db());
        checkQuery.prepare("SELECT id FROM users WHERE username = ?");
        checkQuery.addBindValue(username);
        
        if (checkQuery.exec() && checkQuery.next()) {
            QMessageBox::warning(&registerDialog, "Fehler", "Dieser Benutzername existiert bereits!");
            return;
        }
        
        // Create new user
        QSqlQuery insertQuery(Database::instance().db());
        insertQuery.prepare("INSERT INTO users (username, password_hash, full_name) VALUES (?, ?, ?)");
        insertQuery.addBindValue(username);
        insertQuery.addBindValue(password);
        insertQuery.addBindValue(fullName.isEmpty() ? QVariant() : fullName);
        
        if (insertQuery.exec()) {
            QMessageBox::information(&registerDialog, "Erfolg", 
                "Benutzer '" + username + "' wurde erfolgreich angelegt!");
            registerDialog.accept();
            updateUserList();
            m_userCombo->setCurrentText(username);
            m_passwordEdit->setFocus();
        } else {
            QMessageBox::critical(&registerDialog, "Fehler", "Fehler beim Anlegen des Benutzers!");
        }
    });
    
    registerDialog.exec();
}

QString LoginDialog::getUsername() const {
    QString username = m_userCombo->currentText();
    if (username.contains("(")) {
        username = username.left(username.indexOf("(")).trimmed();
    }
    return username;
}

QString LoginDialog::getPassword() const {
    return m_passwordEdit->text();
}

void LoginDialog::selectLogoFile() {
    QString fileName = QFileDialog::getOpenFileName(this, 
        "Logo auswählen", 
        QDir::homePath(),
        "Bilder (*.png *.jpg *.jpeg *.svg *.ico);;Alle Dateien (*)");
    
    if (!fileName.isEmpty()) {
        QPixmap newLogo(fileName);
        if (!newLogo.isNull()) {
            m_customLogoPath = fileName;
            m_logoLabel->setPixmap(newLogo.scaled(140, 140, Qt::KeepAspectRatio, Qt::SmoothTransformation));
            
            // Save the custom logo path to settings
            QSqlQuery query(Database::instance().db());
            query.prepare("INSERT OR REPLACE INTO settings (key, value) VALUES ('custom_logo_path', ?)");
            query.addBindValue(fileName);
            query.exec();
        } else {
            QMessageBox::warning(this, "Fehler", "Die ausgewählte Datei konnte nicht als Bild geladen werden.");
        }
    }
}

void LoginDialog::selectDatabase() {
    // Use last database directory as starting point
    QSettings settings("CashflowPlanner", "CashflowPlannerPro");
    QString lastDir = QFileInfo(m_databasePath).absolutePath();
    
    QString fileName = QFileDialog::getOpenFileName(this,
        "Datenbank öffnen",
        lastDir,
        "SQLite Datenbank (*.db);;Alle Dateien (*)");
    
    if (!fileName.isEmpty()) {
        m_databasePath = fileName;
        m_dbPathLabel->setText(fileName);
        
        // Save this path as the last used database
        settings.setValue("lastDatabasePath", fileName);
        
        // Close current database and open new one
        Database::instance().close();
        if (Database::instance().open(m_databasePath)) {
            Database::instance().ensureSchema();
            updateUserList();
        } else {
            QMessageBox::critical(this, "Fehler", "Konnte Datenbank nicht öffnen!");
            m_databasePath = "cashflow.db";
            m_dbPathLabel->setText(QDir::currentPath() + "/cashflow.db");
            Database::instance().open(m_databasePath);
            Database::instance().ensureSchema();
        }
    }
}

void LoginDialog::createNewDatabase() {
    // Use last database directory as starting point
    QSettings settings("CashflowPlanner", "CashflowPlannerPro");
    QString lastDir = QFileInfo(m_databasePath).absolutePath();
    
    QString fileName = QFileDialog::getSaveFileName(this,
        "Neue Datenbank erstellen",
        lastDir + "/cashflow_neu.db",
        "SQLite Datenbank (*.db);;Alle Dateien (*)");
    
    if (!fileName.isEmpty()) {
        // Delete file if it exists
        if (QFile::exists(fileName)) {
            QFile::remove(fileName);
        }
        
        m_databasePath = fileName;
        m_dbPathLabel->setText(fileName);
        
        // Save this path as the last used database
        settings.setValue("lastDatabasePath", fileName);
        
        // Close current database and create new one
        Database::instance().close();
        if (Database::instance().open(m_databasePath)) {
            Database::instance().ensureSchema();
            updateUserList();
            QMessageBox::information(this, "Erfolg", 
                "Neue Datenbank wurde erstellt:\n" + QFileInfo(fileName).fileName());
        } else {
            QMessageBox::critical(this, "Fehler", "Konnte Datenbank nicht erstellen!");
            m_databasePath = "cashflow.db";
            m_dbPathLabel->setText(QDir::currentPath() + "/cashflow.db");
            Database::instance().open(m_databasePath);
            Database::instance().ensureSchema();
        }
    }
}

QString LoginDialog::getDatabasePath() const {
    return m_databasePath;
}

// Mouse events removed - using standard title bar