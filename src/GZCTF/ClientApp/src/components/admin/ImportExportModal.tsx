import { Button, Modal, ModalProps, Stack, Textarea } from '@mantine/core'
import { useInputState } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiContentCopy, mdiSend } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useState } from 'react'
import { useTranslation } from 'react-i18next'

type Mode = 'import' | 'export'

interface ImportExportModalProps extends ModalProps {
  mode: Mode
  onSubmitCB?: (text: string) => Promise<unknown> | void
  defaultValue?: string
}

export const ImportExportModal: FC<ImportExportModalProps> = (props) => {
  const { mode, onSubmitCB, defaultValue = '', ...modalProps } = props
  const [text, setText] = useInputState(defaultValue)
  const [disabled, setDisabled] = useState(false)

  const { t } = useTranslation()

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(text)
      showNotification({
        color: 'teal',
        message: t('common.copied'),
        icon: <Icon path={mdiContentCopy} size={1} />,
      })
    } catch (e) {
      showNotification({
        color: 'red',
        message: t('common.error.copy_failed'),
      })
    }
  }

  const handleSubmit = async () => {
    if (!onSubmitCB) return
    setDisabled(true)
    try {
      await onSubmitCB(text)
      showNotification({
        color: 'teal',
        message: t('common.submitted_success'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      modalProps.onClose();
    } catch {
      showNotification({
        color: 'red',
        message: t('common.error.submit_failed'),
      })
    } finally {
      setDisabled(false)
    }
  }

  return (
    <Modal size="lg" title={t('common.text_modal')} {...modalProps}>
      <Stack>
        <Textarea
          autosize
          minRows={8}
          maxRows={18}
          value={text}
          onChange={setText}
          readOnly={mode === 'export'}
          placeholder={t('common.textarea.placeholder')}
          w="100%"
        />
        <Button
          fullWidth
          leftSection={
            <Icon
              path={mode === 'export' ? mdiContentCopy : mdiSend}
              size={1}
            />
          }
          loading={disabled}
          disabled={mode === 'import' && !onSubmitCB}
          onClick={mode === 'export' ? handleCopy : handleSubmit}
        >
          {mode === 'export' ? (t('common.copy')) : (t('common.submit'))}
        </Button>
      </Stack>
    </Modal>
  )
}
