#ifndef LOGINDIALOG_H
#define LOGINDIALOG_H

#include <QDialog>

class QLineEdit;
class QPushButton;
class QLabel;
class QComboBox;
class QToolButton;

class LoginDialog : public QDialog {
    Q_OBJECT

public:
    explicit LoginDialog(QWidget *parent = nullptr);
    
    QString getUsername() const;
    QString getPassword() const;
    QString getDatabasePath() const;
    
private slots:
    void onLoginClicked();
    void onRegisterClicked();
    void updateUserList();
    void selectLogoFile();
    void selectDatabase();
    void createNewDatabase();
    
private:
    QLineEdit *m_usernameEdit;
    QLineEdit *m_passwordEdit;
    QPushButton *m_loginButton;
    QLabel *m_messageLabel;
    QLabel *m_logoLabel;
    QComboBox *m_userCombo;
    QToolButton *m_logoButton;
    QString m_customLogoPath;
    QLabel *m_dbPathLabel;
    QString m_databasePath;
};

#endif // LOGINDIALOG_H