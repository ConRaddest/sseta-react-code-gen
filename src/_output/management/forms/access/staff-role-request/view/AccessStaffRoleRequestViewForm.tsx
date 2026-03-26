"use client"

import { useState, useEffect } from "react"
import { ViewTemplate } from "@sseta/components"
import { useAccessStaffRoleRequest } from "@/contexts/resources/access/AccessStaffRoleRequestContext"
import { AccessStaffRoleRequest } from "@/types/api.types"
import AccessStaffRoleRequestViewLayout from "./AccessStaffRoleRequestViewLayout"

interface AccessStaffRoleRequestViewFormProps {
  staffRoleRequestId: number
  hiddenFields?: string[]
  className?: string
  loading?: boolean
}

export default function AccessStaffRoleRequestViewForm(props: AccessStaffRoleRequestViewFormProps) {
  const { staffRoleRequestId, hiddenFields, className = "px-6 py-4", loading: loadingOverride } = props

  const [record, setRecord] = useState<AccessStaffRoleRequest | null>(null)
  const [loading, setLoading] = useState(false)
  const isLoading = loadingOverride ?? loading

  const { retrieve } = useAccessStaffRoleRequest()

  useEffect(() => {
    const fetchRecord = async () => {
      setLoading(true)
      try {
        const result = await retrieve(staffRoleRequestId)
        if (result) setRecord(result)
      } catch (error) {
        console.error("Failed to fetch staff role request:", error)
      } finally {
        setLoading(false)
      }
    }
    fetchRecord()
  }, [])

  return (
    <ViewTemplate
      layout={AccessStaffRoleRequestViewLayout}
      record={record}
      isLoading={isLoading}
      hiddenFields={hiddenFields}
      className={className}
    />
  )
}
